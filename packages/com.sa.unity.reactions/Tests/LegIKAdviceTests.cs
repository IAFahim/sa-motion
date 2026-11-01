using NUnit.Framework;

namespace SA.Reactions.Tests
{
    public class LegIKAdviceTests
    {
        const float Late = LegIKAdvice.Patience + 1f;
        const float Leg = 0.8f;
        const float GoodReach = 0.16f;
        const float BadReach = 0.95f;

        [Test]
        public void APlantedFootIsWorkingWhateverElseIsTrue()
        {
            Assert.AreEqual(LegIKCause.Working, LegIKAdvice.Diagnose(0f, 0f, BadReach, Leg, true, Late));
        }

        [Test]
        public void NothingIsReportedBeforeThePatienceRunsOut()
        {
            Assert.AreEqual(LegIKCause.Working, LegIKAdvice.Diagnose(0f, 0f, BadReach, Leg, false, LegIKAdvice.Patience - 0.01f));
            Assert.AreEqual(LegIKCause.Working, LegIKAdvice.Diagnose(1f, 1f, GoodReach, Leg, false, 0f));
        }

        [Test]
        public void CausesAreReportedMostObviousFirst()
        {
            Assert.AreEqual(LegIKCause.WeightZero, LegIKAdvice.Diagnose(0f, 0f, BadReach, Leg, false, Late));
            Assert.AreEqual(LegIKCause.ClipsUnticked, LegIKAdvice.Diagnose(1f, 0f, BadReach, Leg, false, Late));
            Assert.AreEqual(LegIKCause.BadFootShape, LegIKAdvice.Diagnose(1f, 1f, BadReach, Leg, false, Late));
            Assert.AreEqual(LegIKCause.NoGround, LegIKAdvice.Diagnose(1f, 1f, GoodReach, Leg, false, Late));
        }

        [Test]
        public void TheKyleBugIsRecognised()
        {
            Assert.AreEqual(LegIKCause.BadFootShape, LegIKAdvice.Diagnose(1f, 1f, 0.9565f, 0.8f, false, 10f));
        }

        [Test]
        public void CorruptNumbersAreTreatedAsZero()
        {
            Assert.AreEqual(LegIKCause.WeightZero, LegIKAdvice.Diagnose(float.NaN, 1f, GoodReach, Leg, false, Late));
            Assert.AreEqual(LegIKCause.ClipsUnticked, LegIKAdvice.Diagnose(1f, float.NaN, GoodReach, Leg, false, Late));
            Assert.AreEqual(LegIKCause.BadFootShape, LegIKAdvice.Diagnose(1f, 1f, float.NaN, Leg, false, Late));
            Assert.AreEqual(LegIKCause.BadFootShape, LegIKAdvice.Diagnose(1f, 1f, GoodReach, 0f, false, Late));
            Assert.AreEqual(LegIKCause.Working, LegIKAdvice.Diagnose(0f, 0f, GoodReach, Leg, false, float.NaN));
        }

        [Test]
        public void EveryProblemSaysWhereToFixIt()
        {
            Assert.IsEmpty(LegIKAdvice.Message(LegIKCause.Working));
            foreach (var cause in new[] { LegIKCause.WeightZero, LegIKCause.ClipsUnticked, LegIKCause.BadFootShape, LegIKCause.NoGround })
            {
                var message = LegIKAdvice.Message(cause);
                Assert.IsTrue(message.Contains("Authoring") || message.Contains("collider"), message);
            }
        }
    }
}
