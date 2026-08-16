namespace StarBound.Multiplayer
{
    // What a MatchGoal requires to be completed — see MatchProgressionService
    // for how each type's target/parameters get generated, and Match for
    // where completion is actually checked.
    public enum MatchGoalType
    {
        TravelAndPay,
        DefeatNamedTarget
    }
}
