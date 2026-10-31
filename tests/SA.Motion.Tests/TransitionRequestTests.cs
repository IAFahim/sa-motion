using SA.Motion.Inertialization;
using Xunit;

namespace SA.Motion.Tests;

public class TransitionRequestTests
{
    static void Same(RequestState expected, RequestState actual)
    {
        Assert.Equal(expected.Key, actual.Key);
        Assert.Equal(expected.Membership, actual.Membership);
        Assert.Equal(expected.Revision, actual.Revision);
        Assert.Equal(expected.Active, actual.Active);
    }

    static RequestState Start(uint key, uint membership)
    {
        var decision = TransitionRequests.Decide(default, key, membership, false, out var next);
        Assert.Equal(RequestDecision.Start, decision);
        return next;
    }

    static RequestDecision Reference(RequestState current, uint desiredKey, uint membership, bool restart, out RequestState next)
    {
        bool live = current.Active != 0;
        bool keyMatch = current.Key == desiredKey;
        bool memberMatch = current.Membership == membership;
        if (!live)
        {
            next = new RequestState { Key = desiredKey, Membership = membership, Revision = 1, Active = 1 };
            return RequestDecision.Start;
        }
        uint bumped = current.Revision >= uint.MaxValue ? 1 : current.Revision + 1;
        if (!keyMatch)
        {
            next = new RequestState { Key = desiredKey, Membership = membership, Revision = bumped, Active = 1 };
            return RequestDecision.Start;
        }
        if (!memberMatch)
        {
            next = current;
            return RequestDecision.Misuse;
        }
        if (restart)
        {
            next = new RequestState { Key = current.Key, Membership = current.Membership, Revision = bumped, Active = current.Active };
            return RequestDecision.Start;
        }
        next = current;
        return RequestDecision.Keep;
    }

    [Fact]
    public void DefaultStateIsInactiveAndStartsWithRevisionOne()
    {
        Assert.Equal(0, default(RequestState).Active);
        var decision = TransitionRequests.Decide(default, 7, 11, false, out var next);
        Assert.Equal(RequestDecision.Start, decision);
        Assert.Equal(7u, next.Key);
        Assert.Equal(11u, next.Membership);
        Assert.Equal(1u, next.Revision);
        Assert.Equal(1, next.Active);
    }

    [Fact]
    public void InactiveStateAlwaysStartsRegardlessOfRestart()
    {
        var stale = new RequestState { Key = 3, Membership = 4, Revision = 99, Active = 0 };
        foreach (bool restart in new[] { false, true })
        {
            var decision = TransitionRequests.Decide(stale, 3, 4, restart, out var next);
            Assert.Equal(RequestDecision.Start, decision);
            Assert.Equal(3u, next.Key);
            Assert.Equal(4u, next.Membership);
            Assert.Equal(1u, next.Revision);
            Assert.Equal(1, next.Active);
        }
    }

    [Fact]
    public void SameKeySameMembershipWithoutRestartKeeps()
    {
        var current = Start(7, 11);
        var decision = TransitionRequests.Decide(current, 7, 11, false, out var next);
        Assert.Equal(RequestDecision.Keep, decision);
        Same(current, next);
    }

    [Fact]
    public void SameKeySameMembershipWithRestartStartsNextRevision()
    {
        var current = Start(7, 11);
        var decision = TransitionRequests.Decide(current, 7, 11, true, out var next);
        Assert.Equal(RequestDecision.Start, decision);
        Assert.Equal(7u, next.Key);
        Assert.Equal(11u, next.Membership);
        Assert.Equal(current.Revision + 1, next.Revision);
        Assert.Equal(1, next.Active);
    }

    [Theory]
    [InlineData(false, 11u)]
    [InlineData(true, 11u)]
    [InlineData(false, 12u)]
    [InlineData(true, 12u)]
    public void DifferentKeyStartsNewRevision(bool restart, uint membership)
    {
        var current = Start(7, 11);
        var decision = TransitionRequests.Decide(current, 8, membership, restart, out var next);
        Assert.Equal(RequestDecision.Start, decision);
        Assert.Equal(8u, next.Key);
        Assert.Equal(membership, next.Membership);
        Assert.Equal(current.Revision + 1, next.Revision);
        Assert.Equal(1, next.Active);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SameKeyDifferentMembershipIsMisuseAndLeavesStateUntouched(bool restart)
    {
        var current = Start(7, 11);
        var decision = TransitionRequests.Decide(current, 7, 12, restart, out var next);
        Assert.Equal(RequestDecision.Misuse, decision);
        Same(current, next);
    }

    [Fact]
    public void RevisionWrapsFromMaxValueToOne()
    {
        var top = new RequestState { Key = 7, Membership = 11, Revision = uint.MaxValue, Active = 1 };
        var restarted = TransitionRequests.Decide(top, 7, 11, true, out var restartedNext);
        Assert.Equal(RequestDecision.Start, restarted);
        Assert.Equal(1u, restartedNext.Revision);
        Assert.Equal(1, restartedNext.Active);
        var moved = TransitionRequests.Decide(top, 8, 11, false, out var movedNext);
        Assert.Equal(RequestDecision.Start, moved);
        Assert.Equal(1u, movedNext.Revision);
        Assert.Equal(8u, movedNext.Key);
    }

    [Fact]
    public void RevisionBelowMaxValueIncrementsNormally()
    {
        var almost = new RequestState { Key = 7, Membership = 11, Revision = uint.MaxValue - 1, Active = 1 };
        var decision = TransitionRequests.Decide(almost, 7, 11, true, out var next);
        Assert.Equal(RequestDecision.Start, decision);
        Assert.Equal(uint.MaxValue, next.Revision);
    }

    [Fact]
    public void RevisionNeverBecomesZero()
    {
        var state = Start(1, 1);
        var random = new Random(20261031);
        for (var i = 0; i < 5000; i++)
        {
            uint key = (uint)random.Next(0, 4);
            uint membership = (uint)random.Next(0, 3);
            bool restart = random.Next(0, 2) == 0;
            TransitionRequests.Decide(state, key, membership, restart, out state);
            Assert.NotEqual(0u, state.Revision);
            Assert.NotEqual(0, state.Active);
        }
    }

    [Fact]
    public void KeepAppliedTwiceGivesSameState()
    {
        var current = Start(7, 11);
        var first = TransitionRequests.Decide(current, 7, 11, false, out var once);
        var second = TransitionRequests.Decide(once, 7, 11, false, out var twice);
        Assert.Equal(RequestDecision.Keep, first);
        Assert.Equal(RequestDecision.Keep, second);
        Same(once, twice);
        Same(current, twice);
    }

    [Fact]
    public void MisuseAppliedTwiceGivesSameState()
    {
        var current = Start(7, 11);
        var first = TransitionRequests.Decide(current, 7, 12, false, out var once);
        var second = TransitionRequests.Decide(once, 7, 12, false, out var twice);
        Assert.Equal(RequestDecision.Misuse, first);
        Assert.Equal(RequestDecision.Misuse, second);
        Same(current, once);
        Same(once, twice);
    }

    [Fact]
    public void LongDeterministicSequenceMatchesReference()
    {
        var random = new Random(1234567);
        var state = default(RequestState);
        for (var i = 0; i < 20000; i++)
        {
            uint key = (uint)random.Next(0, 4);
            uint membership = (uint)random.Next(0, 3);
            bool restart = random.Next(0, 2) == 0;
            var expected = Reference(state, key, membership, restart, out var expectedNext);
            var actual = TransitionRequests.Decide(state, key, membership, restart, out var actualNext);
            Assert.Equal(expected, actual);
            Same(expectedNext, actualNext);
            state = actualNext;
        }
    }

    [Fact]
    public void SequenceNeverLeavesRevisionZeroOrInactive()
    {
        var random = new Random(7654321);
        var state = default(RequestState);
        for (var i = 0; i < 5000; i++)
        {
            uint key = (uint)random.Next(0, 8);
            uint membership = (uint)random.Next(0, 8);
            bool restart = random.Next(0, 4) == 0;
            TransitionRequests.Decide(state, key, membership, restart, out state);
            Assert.NotEqual(0u, state.Revision);
            Assert.NotEqual(0, state.Active);
        }
    }
}
