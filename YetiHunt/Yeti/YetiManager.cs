using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using YetiHunt.Infrastructure;
using Il2CppInterop.Runtime.InteropTypes;
using Object = UnityEngine.Object;

namespace YetiHunt.Yeti
{
    /// <summary>
    /// Manages spawning, tracking, and despawning of hunt yetis.
    /// </summary>
    public class YetiManager : IYetiManager
    {
        private readonly IModLogger _logger;
        private readonly ITypeResolver _typeResolver;
        private readonly IYetiBehaviorController _behaviorController;

        private readonly List<HuntYeti> _huntYetis = new List<HuntYeti>();
        private object _yetiManagerInstance;

        // Collider settings
        private const float COLLIDER_RADIUS = 2f;
        private const float COLLIDER_HEIGHT = 6.5f;
        private const float COLLIDER_Y_OFFSET = 4.5f;

        public IReadOnlyList<HuntYeti> ActiveYetis => _huntYetis;

        public event Action<HuntYeti, Vector3, string> OnYetiHit;

        public YetiManager(IModLogger logger, ITypeResolver typeResolver, IYetiBehaviorController behaviorController)
        {
            _logger = logger;
            _typeResolver = typeResolver;
            _behaviorController = behaviorController;
        }

        public void SpawnYetiForHunt(Vector3 nearPosition, float minDistance, float maxDistance)
        {
            float angle = UnityEngine.Random.Range(0f, 360f) * Mathf.Deg2Rad;
            float distance = UnityEngine.Random.Range(minDistance, maxDistance);
            Vector3 spawnPos = nearPosition + new Vector3(
                Mathf.Cos(angle) * distance,
                0,
                Mathf.Sin(angle) * distance
            );

            _logger.Info($"Spawning hunt yeti {distance:F0}m from player");
            SpawnYetiAt(spawnPos);
        }

        public void SpawnYetiAt(Vector3 position)
        {
            _logger.Info($"Spawning yeti at {position}");

            var manager = GetYetiManagerInstance();
            if (manager == null)
            {
                _logger.Warning("Could not get YetiManager");
                return;
            }

            var spawnMethod = _typeResolver.GetSpawnYetiMethod();
            if (spawnMethod == null)
            {
                _logger.Warning("Server_SpawnYeti method not found");
                return;
            }

            // Get existing yeti IDs before spawning
            var existingYetis = new HashSet<int>();
            foreach (var obj in Object.FindObjectsOfType<GameObject>())
            {
                if (obj != null && obj.name == "Yeti(Clone)")
                    existingYetis.Add(obj.GetInstanceID());
            }

            try
            {
                spawnMethod.Invoke(manager, new object[] { position });

                // Find the newly spawned yeti
                GameObject newYeti = null;
                foreach (var obj in Object.FindObjectsOfType<GameObject>())
                {
                    if (obj != null && obj.name == "Yeti(Clone)" && !existingYetis.Contains(obj.GetInstanceID()))
                    {
                        newYeti = obj;
                        break;
                    }
                }

                if (newYeti != null)
                {
                    _logger.Info($"Found new yeti at {newYeti.transform.position}");

                    var huntYeti = new HuntYeti
                    {
                        GameObject = newYeti,
                        TargetPosition = position,
                        WanderCenter = position,
                        State = YetiMovementState.Pausing,
                        StateTimer = 1f,
                        CurrentDirection = newYeti.transform.forward,
                        TargetDirection = newYeti.transform.forward
                    };

                    DisableYetiBehaviour(newYeti, huntYeti);
                    _huntYetis.Add(huntYeti);
                }
                else
                {
                    _logger.Warning("Could not find spawned yeti");
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"SpawnYeti failed: {ex.Message}");
            }
        }

        public void DespawnAllYetis()
        {
            foreach (var yeti in _huntYetis)
            {
                if (yeti.GameObject != null)
                {
                    _logger.Info($"Despawning yeti at {yeti.GameObject.transform.position}");
                    Object.Destroy(yeti.GameObject);
                }
            }
            _huntYetis.Clear();
        }

        public void Update(float deltaTime)
        {
            // Clean up destroyed yetis
            _huntYetis.RemoveAll(y => y.GameObject == null);

            foreach (var yeti in _huntYetis)
            {
                _behaviorController.ControlYeti(yeti, deltaTime);
            }
        }

        public void ClearYetiManagerInstance()
        {
            _yetiManagerInstance = null;
        }

        public void RaiseYetiHit(HuntYeti yeti, Vector3 hitPosition, string throwerName)
        {
            OnYetiHit?.Invoke(yeti, hitPosition, throwerName);
        }

        private object GetYetiManagerInstance()
        {
            if (_yetiManagerInstance != null) return _yetiManagerInstance;

            var yetiManagerType = _typeResolver.GetYetiManagerType();
            if (yetiManagerType == null) return null;

            var yetiManagerObj = GameObject.Find("Yeti Manager");
            if (yetiManagerObj == null)
            {
                _logger.Warning("Yeti Manager GameObject not found");
                return null;
            }

            var components = yetiManagerObj.GetComponents<Component>();
            foreach (var comp in components)
            {
                if (comp == null) continue;
                try
                {
                    var il2cppType = comp.GetIl2CppType();
                    if (il2cppType?.Name == "YetiManager")
                    {
                        var castMethod = typeof(Il2CppObjectBase).GetMethod("Cast").MakeGenericMethod(yetiManagerType);
                        _yetiManagerInstance = castMethod.Invoke(comp, null);
                        _logger.Info("Got YetiManager instance");
                        return _yetiManagerInstance;
                    }
                }
                catch { }
            }

            return null;
        }

        private void DisableYetiBehaviour(GameObject yetiObj, HuntYeti huntYeti)
        {
            var yetiType = _typeResolver.GetYetiType();
            if (yetiType == null) return;

            var components = yetiObj.GetComponents<Component>();
            foreach (var comp in components)
            {
                if (comp == null) continue;
                try
                {
                    var il2cppType = comp.GetIl2CppType();
                    if (il2cppType?.Name == "Yeti")
                    {
                        var castMethod = typeof(Il2CppObjectBase).GetMethod("Cast").MakeGenericMethod(yetiType);
                        huntYeti.YetiComponent = castMethod.Invoke(comp, null);
                        huntYeti.MoveMethod = yetiType.GetMethod("MoveFixed_Direction");

                        huntYeti.Animator = yetiObj.GetComponent<Animator>();
                        if (huntYeti.Animator != null)
                        {
                            huntYeti.SpeedHash = Animator.StringToHash("Speed");
                            _logger.Info($"Got Yeti animator, Speed hash: {huntYeti.SpeedHash}");
                        }

                        var behaviour = comp.TryCast<Behaviour>();
                        if (behaviour != null)
                        {
                            behaviour.enabled = false;
                            _logger.Info("Disabled Yeti behaviour component!");
                        }
                        break;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Could not disable Yeti: {ex.Message}");
                }
            }

            DisableYetiEffectsSphere(yetiObj);
            AddYetiCollider(yetiObj);
        }

        private void DisableYetiEffectsSphere(GameObject yetiObj)
        {
            Transform yetiTransform = yetiObj.transform;
            for (int i = 0; i < yetiTransform.childCount; i++)
            {
                Transform child = yetiTransform.GetChild(i);
                if (child == null) continue;

                if (child.name.Contains("EFFECT") || child.name.Contains("SPHERE"))
                {
                    _logger.Info($"Found child: {child.name}");

                    var colliders = child.GetComponents<Collider>();
                    foreach (var col in colliders)
                    {
                        if (col != null)
                        {
                            col.enabled = false;
                            _logger.Info($"Disabled collider on {child.name}");
                        }
                    }
                }
            }

            foreach (var obj in Object.FindObjectsOfType<GameObject>())
            {
                if (obj == null) continue;
                if (obj.name == "YETI EFFECTS SPHERE")
                {
                    float dist = Vector3.Distance(obj.transform.position, yetiObj.transform.position);
                    if (dist < 30f)
                    {
                        var colliders = obj.GetComponents<Collider>();
                        foreach (var col in colliders)
                        {
                            if (col != null)
                            {
                                col.enabled = false;
                                _logger.Info($"Disabled collider on YETI EFFECTS SPHERE");
                            }
                        }

                        obj.SetActive(false);
                        _logger.Info("Disabled YETI EFFECTS SPHERE object");
                    }
                }
            }
        }

        private void AddYetiCollider(GameObject yetiObj)
        {
            try
            {
                int playerLayer = 8;
                yetiObj.layer = playerLayer;
                _logger.Info($"Set yeti to layer {playerLayer} (Player)");

                var yetiCollider = yetiObj.AddComponent<CapsuleCollider>();
                yetiCollider.center = new Vector3(0, COLLIDER_Y_OFFSET, 0);
                yetiCollider.radius = COLLIDER_RADIUS;
                yetiCollider.height = COLLIDER_HEIGHT;
                yetiCollider.isTrigger = false;
                _logger.Info($"Added CapsuleCollider to yeti (solid, radius={COLLIDER_RADIUS}, height={COLLIDER_HEIGHT})");
            }
            catch (Exception ex)
            {
                _logger.Warning($"Could not add collider: {ex.Message}");
            }
        }
    }
}
