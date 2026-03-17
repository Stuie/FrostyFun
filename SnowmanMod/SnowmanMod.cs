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

    // Cached hat sources from the shop
    private List<Transform> _hatSources = new();
    private bool _hatSourcesCached;

    private class SnowmanHead
    {
        public Transform BallTransform;
        public Transform FaceTransform;
        public int Id;
        public float OriginalBallX;  // Preserve original X rotation
        public float OriginalBallZ;  // Preserve original Z rotation
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
        _hatSources.Clear();
        _hatSourcesCached = false;

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

            // Randomize the hat on this new snowman
            RandomizeHat(faceTransform);

            // Store original ball rotation before we start modifying it
            Vector3 originalEuler = highestBall.eulerAngles;

            // Add to tracking
            var snowmanHead = new SnowmanHead
            {
                BallTransform = highestBall,
                FaceTransform = faceTransform,
                Id = headId,
                OriginalBallX = originalEuler.x,
                OriginalBallZ = originalEuler.z
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

    private void CacheHatSources()
    {
        if (_hatSourcesCached) return;
        _hatSourcesCached = true;

        // Find the hat shop backboard
        var shopHats = GameObject.Find("Shop (hats)");
        if (shopHats == null)
        {
            Melon<SnowmanModMain>.Logger.Warning("Could not find Shop (hats)");
            return;
        }

        var backboard = shopHats.transform.Find("Backboard");
        if (backboard == null)
        {
            Melon<SnowmanModMain>.Logger.Warning("Could not find Backboard in Shop (hats)");
            return;
        }

        // Find all hat children (names starting with "(Hat)")
        for (int i = 0; i < backboard.childCount; i++)
        {
            var child = backboard.GetChild(i);
            if (child.name.StartsWith("(Hat)") && !child.name.Contains("Missing Prefab"))
            {
                _hatSources.Add(child);
            }
        }

        Melon<SnowmanModMain>.Logger.Msg($"Cached {_hatSources.Count} hat sources from shop");
    }

    private void RandomizeHat(Transform faceTransform)
    {
        CacheHatSources();

        if (_hatSources.Count == 0)
        {
            Melon<SnowmanModMain>.Logger.Warning("No hat sources available for randomization");
            return;
        }

        // Find Face Graphics child
        var faceGraphics = faceTransform.Find("Face Graphics");
        if (faceGraphics == null)
        {
            Melon<SnowmanModMain>.Logger.Warning("Could not find Face Graphics");
            return;
        }

        // Find the current hat on the snowman
        Transform currentHat = null;
        for (int i = 0; i < faceGraphics.childCount; i++)
        {
            var child = faceGraphics.GetChild(i);
            if (child.name.StartsWith("(Hat)"))
            {
                currentHat = child;
                break;
            }
        }

        if (currentHat == null)
        {
            Melon<SnowmanModMain>.Logger.Warning("No existing hat found on snowman");
            return;
        }

        // Pick a random hat source
        int randomIndex = UnityEngine.Random.Range(0, _hatSources.Count);
        var sourceHat = _hatSources[randomIndex];

        // Clone the hat
        var newHat = Object.Instantiate(sourceHat.gameObject, faceGraphics);
        newHat.name = sourceHat.name + " (Randomized)";

        // Copy position and rotation from the original hat
        newHat.transform.localPosition = currentHat.localPosition;
        newHat.transform.localRotation = currentHat.localRotation;
        newHat.transform.localScale = currentHat.localScale;

        // Make sure new hat is active
        newHat.SetActive(true);

        // Disable the original hat
        currentHat.gameObject.SetActive(false);

        Melon<SnowmanModMain>.Logger.Msg($"Randomized hat to: {sourceHat.name}");
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

        // Apply rotation to ball, preserving original X/Z to maintain face orientation
        ball.rotation = Quaternion.Euler(head.OriginalBallX, smoothedY, head.OriginalBallZ);
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
    /// Dump detailed rotation info for debugging face orientation issues.
    /// Bound to F9 key.
    /// </summary>
    private void DumpRotationDebugInfo()
    {
        Melon<SnowmanModMain>.Logger.Msg("=== SNOWMAN DEBUG (F9) ===");
        Melon<SnowmanModMain>.Logger.Msg($"Tracking {_trackedHeads.Count} snowmen");

        if (_playerTransform != null)
        {
            var p = _playerTransform.position;
            Melon<SnowmanModMain>.Logger.Msg($"Player position: ({p.x:F2}, {p.y:F2}, {p.z:F2})");
        }

        int snowmanNum = 0;
        foreach (var head in _trackedHeads)
        {
            snowmanNum++;
            if (head.BallTransform == null || head.FaceTransform == null)
            {
                Melon<SnowmanModMain>.Logger.Msg($"Snowman #{snowmanNum} (ID: {head.Id}): DESTROYED");
                continue;
            }

            var ball = head.BallTransform;
            var face = head.FaceTransform;

            // Ball info
            var ballPos = ball.position;
            var ballEuler = ball.eulerAngles;
            var ballQuat = ball.rotation;

            // Face info
            var faceLocalEuler = face.localEulerAngles;
            var faceForward = face.forward;
            var faceUp = face.up;

            // Find Face Graphics for additional info
            var faceGraphics = face.Find("Face Graphics");
            string faceGraphicsInfo = "NOT FOUND";
            if (faceGraphics != null)
            {
                var fgLocalEuler = faceGraphics.localEulerAngles;
                faceGraphicsInfo = $"({fgLocalEuler.x:F1}, {fgLocalEuler.y:F1}, {fgLocalEuler.z:F1})";
            }

            // Check if face is tilted (face.forward.y should be close to 0 if horizontal)
            float horizontalMag = Mathf.Sqrt(faceForward.x * faceForward.x + faceForward.z * faceForward.z);
            string status = Mathf.Abs(faceForward.y) < 0.3f ? "OK" : $"TILTED (face.forward.y = {faceForward.y:F2})";

            Melon<SnowmanModMain>.Logger.Msg($"\nSnowman #{snowmanNum} (ID: {head.Id}):");
            Melon<SnowmanModMain>.Logger.Msg($"  Ball pos: ({ballPos.x:F2}, {ballPos.y:F2}, {ballPos.z:F2})");
            Melon<SnowmanModMain>.Logger.Msg($"  Ball euler: ({ballEuler.x:F1}, {ballEuler.y:F1}, {ballEuler.z:F1})");
            Melon<SnowmanModMain>.Logger.Msg($"  Ball quat: ({ballQuat.x:F3}, {ballQuat.y:F3}, {ballQuat.z:F3}, {ballQuat.w:F3})");
            Melon<SnowmanModMain>.Logger.Msg($"  Original X/Z: ({head.OriginalBallX:F1}, {head.OriginalBallZ:F1})");
            Melon<SnowmanModMain>.Logger.Msg($"  Face localEuler: ({faceLocalEuler.x:F1}, {faceLocalEuler.y:F1}, {faceLocalEuler.z:F1})");
            Melon<SnowmanModMain>.Logger.Msg($"  Face forward: ({faceForward.x:F2}, {faceForward.y:F2}, {faceForward.z:F2}) [horiz mag: {horizontalMag:F2}]");
            Melon<SnowmanModMain>.Logger.Msg($"  Face up: ({faceUp.x:F2}, {faceUp.y:F2}, {faceUp.z:F2})");
            Melon<SnowmanModMain>.Logger.Msg($"  Face Graphics localEuler: {faceGraphicsInfo}");
            Melon<SnowmanModMain>.Logger.Msg($"  Status: {status}");
        }

        Melon<SnowmanModMain>.Logger.Msg("\n=== END DEBUG ===");
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

        // Also search for all hat objects in the scene
        SearchForHats();
    }

    private void SearchForHats()
    {
        Melon<SnowmanModMain>.Logger.Msg("\n=== SEARCHING FOR HATS ===");

        var allObjects = Object.FindObjectsOfType<GameObject>();
        var hatObjects = new List<string>();

        foreach (var obj in allObjects)
        {
            if (obj == null) continue;
            string nameLower = obj.name.ToLower();
            if (nameLower.Contains("hat") || nameLower.Contains("cap") || nameLower.Contains("helmet") || nameLower.Contains("beanie"))
            {
                string parentPath = GetParentPath(obj.transform);
                hatObjects.Add($"\"{obj.name}\" at {parentPath} (active={obj.activeSelf})");
            }
        }

        if (hatObjects.Count == 0)
        {
            Melon<SnowmanModMain>.Logger.Msg("No hat objects found");
        }
        else
        {
            Melon<SnowmanModMain>.Logger.Msg($"Found {hatObjects.Count} hat-related objects:");
            foreach (var hat in hatObjects)
            {
                Melon<SnowmanModMain>.Logger.Msg($"  - {hat}");
            }
        }

        Melon<SnowmanModMain>.Logger.Msg("=== END HAT SEARCH ===");
    }

    private string GetParentPath(Transform t)
    {
        var parts = new List<string>();
        var current = t.parent;
        while (current != null && parts.Count < 4)
        {
            parts.Insert(0, current.name);
            current = current.parent;
        }
        return parts.Count > 0 ? string.Join("/", parts) : "(root)";
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
