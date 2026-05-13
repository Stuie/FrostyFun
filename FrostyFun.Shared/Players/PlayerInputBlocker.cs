using System;
using UnityEngine;
using FrostyFun.Shared.Il2Cpp;
using FrostyFun.Shared.Logging;

namespace FrostyFun.Shared.Players
{
    public class PlayerInputBlocker
    {
        private const string PlayerInputObjectName = "Player Input";
        private const string CinemachineObjectName = "CinemachineCamera (makes parent null on start)";

        private readonly IModLogger _logger;

        private Component _playerLocalInput;
        private Component _playerCameraControl;
        private bool _playerLocalInputWasEnabled;
        private bool _playerCameraControlWasEnabled;

        public PlayerInputBlocker(IModLogger logger)
        {
            _logger = logger;
        }

        public void Disable()
        {
            try
            {
                var playerInputObj = GameObject.Find(PlayerInputObjectName);
                if (playerInputObj != null)
                {
                    foreach (var comp in playerInputObj.GetComponents<Component>())
                    {
                        if (comp != null && comp.GetIl2CppTypeName() == "PlayerLocalInput")
                        {
                            _playerLocalInput = comp;
                            var behaviour = comp.TryCast<Behaviour>();
                            if (behaviour != null)
                            {
                                _playerLocalInputWasEnabled = behaviour.enabled;
                                behaviour.enabled = false;
                            }
                            break;
                        }
                    }
                }

                var playerObj = PlayerLocator.FindLocal();
                if (playerObj != null)
                {
                    foreach (var comp in playerObj.GetComponents<Component>())
                    {
                        if (comp != null && comp.GetIl2CppTypeName() == "PlayerCameraControl")
                        {
                            _playerCameraControl = comp;
                            var behaviour = comp.TryCast<Behaviour>();
                            if (behaviour != null)
                            {
                                _playerCameraControlWasEnabled = behaviour.enabled;
                                behaviour.enabled = false;
                            }
                            break;
                        }
                    }
                }

                var cinemachineObj = GameObject.Find(CinemachineObjectName);
                if (cinemachineObj != null)
                {
                    foreach (var comp in cinemachineObj.GetComponents<Component>())
                    {
                        string typeName = comp.GetIl2CppTypeName();
                        if (typeName.Contains("Cinemachine") || typeName.Contains("Input"))
                        {
                            var behaviour = comp.TryCast<Behaviour>();
                            if (behaviour != null && behaviour.enabled)
                                behaviour.enabled = false;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"PlayerInputBlocker.Disable: {ex.Message}");
            }
        }

        public void Restore()
        {
            try
            {
                if (_playerLocalInput != null)
                {
                    var behaviour = _playerLocalInput.TryCast<Behaviour>();
                    if (behaviour != null)
                        behaviour.enabled = _playerLocalInputWasEnabled;
                }

                if (_playerCameraControl != null)
                {
                    var behaviour = _playerCameraControl.TryCast<Behaviour>();
                    if (behaviour != null)
                        behaviour.enabled = _playerCameraControlWasEnabled;
                }

                var cinemachineObj = GameObject.Find(CinemachineObjectName);
                if (cinemachineObj != null)
                {
                    foreach (var comp in cinemachineObj.GetComponents<Component>())
                    {
                        string typeName = comp.GetIl2CppTypeName();
                        if (typeName.Contains("Cinemachine") || typeName.Contains("Input"))
                        {
                            var behaviour = comp.TryCast<Behaviour>();
                            if (behaviour != null)
                                behaviour.enabled = true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"PlayerInputBlocker.Restore: {ex.Message}");
            }
        }

        public void Reset()
        {
            _playerLocalInput = null;
            _playerCameraControl = null;
        }
    }
}
