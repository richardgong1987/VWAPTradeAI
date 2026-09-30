using System.Linq;
using System.Threading.Tasks;
using cAlgo.Robots;
using Xunit;

namespace VWAPTradeAI.Tests.Approval {
    // Decisions cross from the relay link's thread to the cBot thread, which takes them in order.
    public class DecisionInboxTests {
        private static TradeDecisionModel Approve(string signalId) => new(signalId, isApproved: true);

        [Fact]
        public void take_all_hands_out_every_decision_once_oldest_first() {
            var inbox = new DecisionInbox();
            inbox.Post(Approve("first"));
            inbox.Post(Approve("second"));

            Assert.Equal(new[] { "first", "second" }, inbox.TakeAll().Select(decision => decision.SignalId));
            Assert.Empty(inbox.TakeAll());
        }

        [Fact]
        public async Task a_decision_posted_from_another_thread_is_taken_on_this_one() {
            var inbox = new DecisionInbox();

            await Task.Run(() => inbox.Post(Approve("abc123")));

            Assert.Equal("abc123", Assert.Single(inbox.TakeAll()).SignalId);
        }
    }
}
