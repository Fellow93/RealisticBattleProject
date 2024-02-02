namespace RBMAI
{
    public class Posture
    {
        public float posture;
        public float maxPosture = 100f;
        public float regenPerTick = 0.01f;
        public int maxPostureLossCount = 0;
        public float lastPostureLossTime = 0f;

        public Posture()
        {
            this.posture = this.maxPosture;
        }

        public Posture(float maxPosture, float regenPerTick)
        {
            this.maxPosture = maxPosture;
            this.regenPerTick = regenPerTick;
            this.posture = this.maxPosture;
        }


    }
}