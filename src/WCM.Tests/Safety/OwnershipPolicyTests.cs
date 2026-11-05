using WCM.Domain.Policies;

namespace WCM.Tests.Safety;

public class OwnershipPolicyTests
{
    [Theory]
    [InlineData(OwnershipState.InstalledProtected)]
    [InlineData(OwnershipState.SharedProtected)]
    [InlineData(OwnershipState.UnknownPreserved)]
    [InlineData(OwnershipState.UserConfirmedOwnership)]
    [InlineData(OwnershipState.UserRejectedMapping)]
    [InlineData("SomethingNew")]
    public void Protected_states_block_cleanup_even_with_top_score_rule_and_approval(string state) =>
        Assert.False(OwnershipPolicy.Evaluate(state, 100, false, true, true).IsAllowed);

    [Theory]
    [InlineData(OwnershipState.ConfirmedOrphanByHistory, true, true, false, true)]
    [InlineData(OwnershipState.ConfirmedOrphanByHistory, false, true, false, false)]
    [InlineData(OwnershipState.PotentialOrphan, true, false, false, false)]
    [InlineData(OwnershipState.CacheCandidate, true, true, true, false)]
    public void Candidates_need_a_rule_and_approval_and_an_unprotected_path(string state, bool rule, bool approved, bool protectedPath, bool expected) =>
        Assert.Equal(expected, OwnershipPolicy.Evaluate(state, 0, protectedPath, rule, approved).IsAllowed);

    [Fact]
    public void Scores_stay_in_range_and_are_explained()
    {
        Assert.Equal(0, OwnershipScorer.Score([OwnershipScorer.Create(EvidenceType.SharedActive, "x")]));
        Assert.Equal(100, OwnershipScorer.Score(
        [
            OwnershipScorer.Create(EvidenceType.ExactSignature, "x"), OwnershipScorer.Create(EvidenceType.ExecutableMetadata, "x"),
            OwnershipScorer.Create(EvidenceType.Publisher, "x"), OwnershipScorer.Create(EvidenceType.KnownPath, "x"),
            OwnershipScorer.Create(EvidenceType.Historical, "x"), OwnershipScorer.Create(EvidenceType.InstallPath, "x"),
            OwnershipScorer.Create(EvidenceType.FolderName, "x"),
        ]));
        Assert.Equal("Strong evidence", OwnershipScorer.Band(90));
        Assert.Equal("High evidence", OwnershipScorer.Band(70));
        Assert.Equal("Possible", OwnershipScorer.Band(40));
        Assert.Equal("Weak/unknown", OwnershipScorer.Band(39));
    }
}
