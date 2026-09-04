namespace SharedKernel.Reporting.Csv.Tests;

/// <summary>Small immutable row type shared by this project's tests.</summary>
internal sealed record TestRow(int Id, string Name, decimal Amount, DateTime When);
