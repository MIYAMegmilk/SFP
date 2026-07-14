namespace SFP.Simulation
{
    // Wire tag for DeviceRpcRelay.DeviceCommand (SFP.Presentation). Kept in Simulation so both
    // the pure-sim tests and the Presentation/Gameplay layers can reference the same values
    // without Simulation depending on UnityEngine/Netcode.
    public enum DeviceCommandKind : byte
    {
        // Helm (SteeringInteraction)
        SetThrottle,
        SetRudder,
        SetDesiredDepth,
        SetDesiredHeading,
        SetDesiredSpeed,
        ToggleAutoPilot,
        ToggleDepthHold,

        // Reactor (ReactorInteraction)
        SetReactorFission,
        SetReactorTurbine,

        // Ballast (BallastInteraction)
        SetBallastTarget,

        // Bilge pump (PumpInteraction)
        TogglePump,

        // Airlock (AirlockInteraction)
        AirlockFlood,
        AirlockDrain,

        // HVAC
        ToggleO2Generator,
        ToggleCO2Scrubber,
        ToggleVent,

        // Firefighting (ExtinguisherInteraction)
        Extinguish,

        // Fixed suppression system (SuppressionInteraction)
        ToggleSuppression,

        // Sonar (SonarInteraction)
        ToggleSonarActive,
        ToggleSonarPassive,

        // Turret (TurretInteraction)
        SetTurretRotation,
        SetTurretElevation,
        FireTurret,

        // Crew (CrewCommandInteraction)
        IssueCrewOrder,
        CancelCrewOrder,

        // Doors/hatches (DoorInteraction)
        ToggleDoor,

        // Fabricator (FabricatorInteraction)
        StartCraft,

        // Diving suit lockers (DivingSuitInteraction)
        TakeSuit,
        ReturnSuit,
    }
}
