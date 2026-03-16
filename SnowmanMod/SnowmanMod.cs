using System.Collections.Generic;
using MelonLoader;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SnowmanMod;

public class SnowmanModMain : MelonMod
{
    private const float SmoothSpeed = 3f;
    private const float ScanInterval = 1f; // How often to scan for new snowmen

    private bool _inGame;
    private float _lastScanTime;

    // Tracked snowman heads with their Face child transforms
    private List<SnowmanHead> _trackedHeads = new();
    private HashSet<int> _knownBallIds = new(); // Track which balls we've already processed

    private Transform _playerTransform;
    private bool _foundRealPlayer;

    private class SnowmanHead
    {
        public Transform BallTransform;
        public Transform FaceTransform;
        public int Id;
    }

    public override void OnInitializeMelon()
    {
        Melon<SnowmanModMain>.Logger.Msg("SnowmanMod initialized - snowmen will automatically track the player");
    }

    public override void OnSceneWasLoaded(int buildIndex, string sceneName)
    {
        Melon<SnowmanModMain>.Logger.Msg($"Scene loaded: {sceneName}");

        _inGame = !sceneName.ToLower().Contains("boot") &&
                  !sceneName.ToLower().Contains("menu") &&
                  !sceneName.ToLower().Contains("loading");

        _trackedHeads.Clear();
        _knownBallIds.Clear();
        _playerTransform = null;
        _foundRealPlayer = false;
        _lastScanTime = 0;

        if (_inGame)
        {
            Melon<SnowmanModMain>.Logger.Msg("In game - snowman tracking active");
        }
    }

    public override void OnUpdate()
    {
        if (!_inGame) return;

        FindPlayer();

        // Periodically scan for new snowmen
        float currentTime = Time.time;
        if (currentTime - _lastScanTime > ScanInterval)
        {
            ScanForNewSnowmen();
            _lastScanTime = currentTime;
        }

        // Update tracking for all captured snowmen
        UpdateTracking();
    }

    private void FindPlayer()
    {
        if (_foundRealPlayer && _playerTransform != null) return;

        try
        {
            var player = GameObject.Find("Player Networked(Clone)");
            if (player != null)
            {
                _playerTransform = player.transform;
                _foundRealPlayer = true;
            }
        }
        catch { }
    }

    private void ScanForNewSnowmen()
    {
        if (_playerTransform == null) return;

        // Find all snowman balls
        var allBalls = new List<Transform>();
        var allObjects = Object.FindObjectsOfType<GameObject>();

        foreach (var obj in allObjects)
        {
            if (obj == null) continue;
            if (obj.name == "Snowman Ball(Clone)")
            {
                allBalls.Add(obj.transform);
            }
        }

        if (allBalls.Count == 0) return;

        // Group balls by X,Z position and find heads (highest Y)
        var processedInThisScan = new HashSet<int>();

        foreach (var ball in allBalls)
        {
            int ballId = ball.GetInstanceID();
            if (processedInThisScan.Contains(ballId)) continue;

            // Find all balls at this X,Z position
            Transform highestBall = ball;
            float highestY = ball.position.y;
            var group = new List<Transform> { ball };

            foreach (var other in allBalls)
            {
                if (other == ball) continue;
                if (processedInThisScan.Contains(other.GetInstanceID())) continue;

                float dx = Mathf.Abs(other.position.x - ball.position.x);
                float dz = Mathf.Abs(other.position.z - ball.position.z);

                if (dx < 0.5f && dz < 0.5f)
                {
                    group.Add(other);
                    if (other.position.y > highestY)
                    {
                        highestBall = other;
                        highestY = other.position.y;
                    }
                }
            }

            // Mark all balls in this group as processed
            foreach (var b in group)
            {
                processedInThisScan.Add(b.GetInstanceID());
            }

            // Only process if this is a complete snowman (2+ balls stacked)
            if (group.Count < 2) continue;

            int headId = highestBall.GetInstanceID();

            // Skip if we're already tracking this head
            if (_knownBallIds.Contains(headId)) continue;

            // Find the Face child
            Transform faceTransform = FindFaceChild(highestBall);
            if (faceTransform == null) continue;

            // Add to tracking
            var snowmanHead = new SnowmanHead
            {
                BallTransform = highestBall,
                FaceTransform = faceTransform,
                Id = headId
            };
            _trackedHeads.Add(snowmanHead);
            _knownBallIds.Add(headId);

            Melon<SnowmanModMain>.Logger.Msg($"Now tracking snowman head {headId}");
        }

        // Clean up destroyed snowmen
        CleanupDestroyedSnowmen();
    }

    private void CleanupDestroyedSnowmen()
    {
        for (int i = _trackedHeads.Count - 1; i >= 0; i--)
        {
            var head = _trackedHeads[i];
            if (head.BallTransform == null || head.FaceTransform == null)
            {
                _knownBallIds.Remove(head.Id);
                _trackedHeads.RemoveAt(i);
            }
        }
    }

    private Transform FindFaceChild(Transform ball)
    {
        for (int i = 0; i < ball.childCount; i++)
        {
            var child = ball.GetChild(i);
            if (child.name == "Face")
            {
                return child;
            }
        }
        return null;
    }

    private void UpdateTracking()
    {
        if (_trackedHeads.Count == 0 || _playerTransform == null) return;

        Vector3 playerPos = _playerTransform.position;

        foreach (var head in _trackedHeads)
        {
            if (head.BallTransform == null || head.FaceTransform == null) continue;
            RotateFaceTowardPlayer(head, playerPos);
        }
    }

    private void RotateFaceTowardPlayer(SnowmanHead head, Vector3 playerPos)
    {
        Transform ball = head.BallTransform;
        Transform face = head.FaceTransform;

        // Direction from ball to player (horizontal only)
        Vector3 toPlayer = playerPos - ball.position;
        toPlayer.y = 0;

        if (toPlayer.sqrMagnitude < 0.01f) return;

        // Current face forward direction (horizontal)
        Vector3 faceForward = face.forward;
        faceForward.y = 0;
        faceForward.Normalize();

        // Target direction
        Vector3 targetDir = toPlayer.normalized;

        // Calculate how much we need to rotate the BALL to make the FACE point at player
        float currentAngle = Mathf.Atan2(faceForward.x, faceForward.z) * Mathf.Rad2Deg;
        float targetAngle = Mathf.Atan2(targetDir.x, targetDir.z) * Mathf.Rad2Deg;
        float angleDiff = Mathf.DeltaAngle(currentAngle, targetAngle);

        // Apply this rotation to the ball
        float currentBallY = ball.eulerAngles.y;
        float targetBallY = currentBallY + angleDiff;

        // Smooth rotation
        float smoothedY = Mathf.LerpAngle(currentBallY, targetBallY, SmoothSpeed * Time.deltaTime);

        // Apply rotation to ball (keep it upright)
        ball.rotation = Quaternion.Euler(0, smoothedY, 0);
    }

    // ========== Methods kept for future mod menu / command integration ==========

    /// <summary>
    /// Manually trigger a scan and capture of all snowmen.
    /// Can be called from a mod menu or chat command.
    /// </summary>
    public void CaptureSnowmen()
    {
        FindPlayer();
        if (_playerTransform == null)
        {
            Melon<SnowmanModMain>.Logger.Warning("Cannot capture - player not found");
            return;
        }

        // Clear existing tracking and rescan
        _trackedHeads.Clear();
        _knownBallIds.Clear();
        ScanForNewSnowmen();

        Melon<SnowmanModMain>.Logger.Msg($"Captured {_trackedHeads.Count} snowmen");
    }

    /// <summary>
    /// Stop all snowman tracking.
    /// Can be called from a mod menu or chat command.
    /// </summary>
    public void StopTracking()
    {
        _trackedHeads.Clear();
        _knownBallIds.Clear();
        Melon<SnowmanModMain>.Logger.Msg("Tracking stopped");
    }

    /// <summary>
    /// Show debug information about tracked snowmen.
    /// Can be called from a mod menu or chat command.
    /// </summary>
    public void ShowDebugInfo()
    {
        Melon<SnowmanModMain>.Logger.Msg("=== DEBUG INFO ===");
        Melon<SnowmanModMain>.Logger.Msg($"Tracking {_trackedHeads.Count} heads");

        if (_playerTransform != null)
        {
            var p = _playerTransform.position;
            Melon<SnowmanModMain>.Logger.Msg($"Player at ({p.x:F1}, {p.y:F1}, {p.z:F1})");
        }

        foreach (var head in _trackedHeads)
        {
            if (head.BallTransform == null || head.FaceTransform == null) continue;

            var ballPos = head.BallTransform.position;
            var ballRot = head.BallTransform.eulerAngles;
            var faceForward = head.FaceTransform.forward;
            float faceAngle = Mathf.Atan2(faceForward.x, faceForward.z) * Mathf.Rad2Deg;

            string playerInfo = "";
            if (_playerTransform != null)
            {
                Vector3 toPlayer = _playerTransform.position - ballPos;
                toPlayer.y = 0;
                float angleToPlayer = Mathf.Atan2(toPlayer.x, toPlayer.z) * Mathf.Rad2Deg;
                float diff = Mathf.DeltaAngle(faceAngle, angleToPlayer);
                playerInfo = $" | ToPlayer={angleToPlayer:F1}, Diff={diff:F1}";
            }

            Melon<SnowmanModMain>.Logger.Msg($"Head {head.Id}: ballY={ballRot.y:F1}, faceAngle={faceAngle:F1}{playerInfo}");
        }

        Melon<SnowmanModMain>.Logger.Msg("=== END DEBUG ===");
    }

    /// <summary>
    /// Inspect the hierarchy of all snowballs.
    /// Can be called from a mod menu or chat command.
    /// </summary>
    public void InspectSnowballHierarchy()
    {
        Melon<SnowmanModMain>.Logger.Msg("=== SNOWBALL HIERARCHY ===");

        var allObjects = Object.FindObjectsOfType<GameObject>();
        int count = 0;

        foreach (var obj in allObjects)
        {
            if (obj == null) continue;
            if (obj.name != "Snowman Ball(Clone)") continue;

            count++;
            var t = obj.transform;
            Melon<SnowmanModMain>.Logger.Msg($"\n--- Ball {obj.GetInstanceID()} at Y={t.position.y:F1} ---");

            Melon<SnowmanModMain>.Logger.Msg($"Children ({t.childCount}):");
            LogChildrenRecursive(t, 1);
        }

        if (count == 0)
        {
            Melon<SnowmanModMain>.Logger.Msg("No snowballs found");
        }

        Melon<SnowmanModMain>.Logger.Msg("=== END HIERARCHY ===");
    }

    private void LogChildrenRecursive(Transform parent, int depth)
    {
        string indent = new string(' ', depth * 2);

        for (int i = 0; i < parent.childCount; i++)
        {
            var child = parent.GetChild(i);
            if (child == null) continue;

            var localPos = child.localPosition;
            var localRot = child.localEulerAngles;

            Melon<SnowmanModMain>.Logger.Msg($"{indent}- \"{child.name}\" pos=({localPos.x:F2}, {localPos.y:F2}, {localPos.z:F2}) rot=({localRot.x:F1}, {localRot.y:F1}, {localRot.z:F1})");

            if (child.childCount > 0 && depth < 3)
            {
                LogChildrenRecursive(child, depth + 1);
            }
        }
    }
}
