using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using SavableObservable.Samples;
using SavableObservable.Tests.Contracts;

namespace SavableObservable.Tests {
    [TestFixture]
    [Category("MmvcContract")]
    public class SamplePlayerContractTests : MmvcTriadContractTestsBase<SamplePlayerModel, SamplePlayerLogic, SamplePlayerPresenter> {
        [Test]
        public void Sample_LastRoll_NotifiesOnSameValue_WithPreviousEqualToValue() {
            Host.SetActive(true);
            Presenter.EnsureInitialized();

            Logic.Roll(4);
            Assert.AreEqual(1, Presenter.RollEventsCount);
            Assert.AreEqual(0, Presenter.RepeatedRollCount, "A new roll is not a repeat.");

            Logic.Roll(4);
            Assert.AreEqual(2, Presenter.RollEventsCount, "[NotifyOnSameValue]: the repeated roll still notifies.");
            Assert.AreEqual(1, Presenter.RepeatedRollCount, "The handler sees previous == value for a repeated roll.");
        }

        [Test]
        public void Sample_DefaultFields_StaySilentOnEqualAssignment() {
            Host.SetActive(true);
            Presenter.EnsureInitialized();
            var before = Presenter.HealthChangeEventsCount;

            Model.health.Value = Model.health.Value;

            Assert.AreEqual(before, Presenter.HealthChangeEventsCount, "health has no [NotifyOnSameValue]: an equal assignment is silent.");
        }
    }

    [TestFixture]
    [Category("MmvcContract")]
    public class SamplePlayerPersistenceContractTests : MmvcPersistenceContractTestsBase<SamplePlayerModel, SamplePlayerLoader> {
        private SampleSaveDto _savedDto;

        protected override void MutateModelForSave(SamplePlayerModel model) {
            model.playerName.Value = "TestPersistencePlayer";
            model.health.Value = 77;
            model.energy.Value = 0.5f;
            model.isShieldActive.Value = true;
            model.inventory.Clear();
            model.inventory.Add("SuperPotion");
            model.inventory.Add("MagicKey");
            model.score = 1234;
        }

        protected override void MutateModelAfterSave(SamplePlayerModel model) {
            model.playerName.Value = "DifferentPlayer";
            model.health.Value = 10;
            model.energy.Value = 0.1f;
            model.isShieldActive.Value = false;
            model.inventory.Clear();
            model.inventory.Add("EmptyBottle");
            model.score = 0;
        }

        protected override void AssertModelRestored(SamplePlayerModel model) {
            Assert.AreEqual("TestPersistencePlayer", model.playerName.Value);
            Assert.AreEqual(77, model.health.Value);
            Assert.AreEqual(0.5f, model.energy.Value, 0.001f);
            Assert.IsTrue(model.isShieldActive.Value);
            Assert.AreEqual(2, model.inventory.Count);
            Assert.AreEqual("SuperPotion", model.inventory[0]);
            Assert.AreEqual("MagicKey", model.inventory[1]);
            Assert.AreEqual(1234, model.score);
        }

        protected override bool PerformSave(SamplePlayerLoader loader, SamplePlayerModel model) {
            _savedDto = loader.SaveCurrentState();
            return _savedDto != null;
        }

        protected override void PerformLoad(SamplePlayerLoader loader, SamplePlayerModel model) {
            loader.LoadSampleState(_savedDto);
        }
    }

    [TestFixture]
    [Category("MmvcContract")]
    public class FrameworkAssemblyAuditTests : MmvcAssemblyAuditTestsBase {
        protected override Assembly[] TargetAssemblies => new[] {
            typeof(BaseObservableDataModel).Assembly,
            typeof(SamplePlayerModel).Assembly
        };
    }
}
