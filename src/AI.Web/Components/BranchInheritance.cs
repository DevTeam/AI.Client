namespace AI.Web.Components;

/// <summary>Where a branch setting shown in the composer comes from.</summary>
/// <param name="Overridden">The branch sets the value itself.</param>
/// <param name="From">The branch the value comes from, or would come from once the branch inherits it again.</param>
public sealed record BranchInheritance(bool Overridden, string From);
