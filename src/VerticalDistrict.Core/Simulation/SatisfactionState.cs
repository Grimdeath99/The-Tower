namespace VerticalDistrict.Core.Simulation;

public sealed record RoomExperience(long RoomId, int Satisfaction, long LastTravelSeconds, int CompletionScore);
public sealed record ComplaintState(long Id, long RoomId, string Code, string Message, long CreatedTick, long? ResolvedTick);
public sealed record SatisfactionSample(long Tick, int Score);
public sealed record SatisfactionSnapshot(long NextComplaintId, RoomExperience[] Experiences, ComplaintState[] Complaints, SatisfactionSample[] History);
public sealed record SatisfactionFactor(string Name, int Score);
public sealed record DemandAssessment(int Score, int Satisfaction, IReadOnlyList<SatisfactionFactor> Factors, IReadOnlyList<string> Reasons);

