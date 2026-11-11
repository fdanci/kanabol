namespace Kanabol.Api.Controllers;

/// <summary>Minutes, from 15 to 60,000; null takes the estimate away, only before the task entered În lucru.</summary>
public sealed record SetTaskEstimateRequest(int? EstimatedMinutes);
