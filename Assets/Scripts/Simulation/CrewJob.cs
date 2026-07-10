namespace SFP.Simulation
{
    public enum CrewJobKind
    {
        Captain,
        Engineer,
        Mechanic,
        DamageControl,
    }

    public static class CrewJob
    {
        // Rows: Captain, Engineer, Mechanic, DamageControl
        // Cols: FightFire, RepairBreach, OperatePump, OperateReactor
        static readonly float[,] Proficiency =
        {
            { 1.0f, 1.0f, 1.0f, 1.0f },
            { 0.5f, 0.7f, 1.3f, 1.5f },
            { 0.6f, 1.5f, 1.2f, 0.6f },
            { 1.5f, 0.8f, 0.8f, 0.5f },
        };

        public static float GetProficiency(CrewJobKind job, CrewTaskKind task)
        {
            int col;
            switch (task)
            {
                case CrewTaskKind.FightFire:      col = 0; break;
                case CrewTaskKind.RepairBreach:    col = 1; break;
                case CrewTaskKind.OperatePump:     col = 2; break;
                case CrewTaskKind.OperateReactor:  col = 3; break;
                default: return 1f;
            }
            int row = (int)job;
            if (row < 0 || row >= Proficiency.GetLength(0)) return 1f;
            return Proficiency[row, col];
        }

        public static string GetLabel(CrewJobKind job)
        {
            switch (job)
            {
                case CrewJobKind.Captain:       return "CPT";
                case CrewJobKind.Engineer:      return "ENG";
                case CrewJobKind.Mechanic:      return "MEC";
                case CrewJobKind.DamageControl: return "DMC";
                default: return "???";
            }
        }
    }
}
