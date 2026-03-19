using System.Reflection;
using UnityEngine;

namespace YetiHunt.Yeti
{
    /// <summary>
    /// Data class tracking a spawned yeti and its movement state.
    /// </summary>
    public class HuntYeti
    {
        public GameObject GameObject { get; set; }
        public object YetiComponent { get; set; }
        public MethodInfo MoveMethod { get; set; }
        public Animator Animator { get; set; }
        public int SpeedHash { get; set; }
        public Vector3 TargetPosition { get; set; }
        public Vector3 WanderCenter { get; set; }
        public float StateTimer { get; set; }
        public YetiMovementState State { get; set; }
        public Vector3 CurrentDirection { get; set; }
        public Vector3 TargetDirection { get; set; }
    }
}
