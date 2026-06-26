namespace CreativeLongform.Application.Agent;

/// <summary>Scales agent turn and check budgets by draft complexity.</summary>
public static class AgentSessionBudget
{
    public const int MaxTurnsCap = 48;
    public const int MaxChecksCap = 16;

    public static int ScaleTurns(int baseTurns, int paragraphCount) =>
        Math.Min(MaxTurnsCap, baseTurns + Math.Max(0, paragraphCount - 5) / 3);

    public static int ScaleChecks(int baseChecks, int paragraphCount) =>
        Math.Min(MaxChecksCap, baseChecks + paragraphCount / 10);
}
