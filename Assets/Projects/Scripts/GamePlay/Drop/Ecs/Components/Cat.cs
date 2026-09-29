using Unity.Entities;

namespace Projects.Scripts.GamePlay.Drop.Ecs.Components
{
    public struct Cat : IComponentData
    {
        public Entity Stage;
        public int Slot;
        public int Column;
        public int Level;
        public int BouncesRemaining;
        public int ImpactDamage;
        public bool IsSpent;
        public int ImpactSerial;
        public float X;
        public float HorizontalVelocity;
        public float Y;
        public float PreviousY;
        public float Velocity;
        public float FallSpeed;
        public float FallAcceleration;
        public float BounceOriginY;
        public float BounceCeilingY;
        public float BounceHeight;
        public float BounceDuration;
        public float BounceElapsed;
        public int BounceOriginRow;
        public bool IsBouncing;
    }
}
