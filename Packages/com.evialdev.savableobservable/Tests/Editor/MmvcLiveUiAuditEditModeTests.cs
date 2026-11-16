using System;
using System.Reflection;
using NUnit.Framework;
using SavableObservable;
using SavableObservable.Tests.Contracts;
using UnityEngine;
using UnityEngine.UI;

namespace SavableObservable.Tests {
    public enum SampleUiEnum {
        First,
        Second,
        Third
    }

    [LiveUiAuditSkip("Skipping sample skipped presenter")]
    public class SampleSkippedPresenter : MonoBehaviour { }

    public class SampleNormalPresenter : MonoBehaviour {
        [AutoBind("score")] public Slider scoreSlider;
        [AutoBind("flag")] public Toggle flagToggle;
        [AutoBind("name")] [LiveUiAuditSkip("Skipped name field")] public Text nameText;
    }

    [LiveUiAuditSkip("Skipping sample skipped model")]
    public class SampleSkippedModel : BaseObservableDataModel { }

    public class SampleNormalModel : BaseObservableDataModel {
        public ObservableVariable<int> score = new ObservableVariable<int>();
        public ObservableVariable<bool> flag = new ObservableVariable<bool>();
        public ObservableVariable<string> name = new ObservableVariable<string>();
    }

    [TestFixture]
    [Category("MmvcContract")]
    public class MmvcLiveUiAuditEditModeTests {
        [Test]
        public void SampleGeneration_Bool_InvertsCurrentValue() {
            Assert.AreEqual(true, MmvcLiveUiAudit.GenerateSampleValue(typeof(bool), false));
            Assert.AreEqual(false, MmvcLiveUiAudit.GenerateSampleValue(typeof(bool), true));
        }

        [Test]
        public void SampleGeneration_Int_WithoutSlider_IncrementsValue() {
            Assert.AreEqual(11, MmvcLiveUiAudit.GenerateSampleValue(typeof(int), 10, null, 1));
            Assert.AreEqual(12, MmvcLiveUiAudit.GenerateSampleValue(typeof(int), 10, null, 2));
        }

        [Test]
        public void SampleGeneration_Int_WithSlider_ClampsWithinBounds() {
            var go = new GameObject("SliderHost");
            try {
                var slider = go.AddComponent<Slider>();
                slider.minValue = 0;
                slider.maxValue = 10;

                object sample = MmvcLiveUiAudit.GenerateSampleValue(typeof(int), 5, slider, 1);
                Assert.IsInstanceOf<int>(sample);
                int val = (int)sample;
                Assert.IsTrue(val >= 0 && val <= 10);
                Assert.AreNotEqual(5, val);

                // Near maximum boundary
                object atMax = MmvcLiveUiAudit.GenerateSampleValue(typeof(int), 10, slider, 1);
                int atMaxVal = (int)atMax;
                Assert.IsTrue(atMaxVal >= 0 && atMaxVal <= 10);
                Assert.AreNotEqual(10, atMaxVal);
            } finally {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void SampleGeneration_Float_WithSlider_ClampsWithinBounds() {
            var go = new GameObject("SliderHostFloat");
            try {
                var slider = go.AddComponent<Slider>();
                slider.minValue = 1.0f;
                slider.maxValue = 5.0f;

                object sample = MmvcLiveUiAudit.GenerateSampleValue(typeof(float), 3.0f, slider, 1);
                Assert.IsInstanceOf<float>(sample);
                float val = (float)sample;
                Assert.IsTrue(val >= 1.0f && val <= 5.0f);
                Assert.AreNotEqual(3.0f, val);
            } finally {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void SampleGeneration_Enum_CyclesThroughValues() {
            object next = MmvcLiveUiAudit.GenerateSampleValue(typeof(SampleUiEnum), SampleUiEnum.First, null, 1);
            Assert.AreEqual(SampleUiEnum.Second, next);

            object loop = MmvcLiveUiAudit.GenerateSampleValue(typeof(SampleUiEnum), SampleUiEnum.Third, null, 1);
            Assert.AreEqual(SampleUiEnum.First, loop);
        }

        [Test]
        public void SampleGeneration_String_ProducesDistinctSample() {
            object s1 = MmvcLiveUiAudit.GenerateSampleValue(typeof(string), "init", null, 1);
            object s2 = MmvcLiveUiAudit.GenerateSampleValue(typeof(string), "init", null, 2);
            Assert.AreEqual("audit_sample_1", s1);
            Assert.AreEqual("audit_sample_2", s2);
        }

        [Test]
        public void SkipRules_Presenter_SkipsWhenAttributeOrOptionsMatched() {
            var options = new LiveUiAuditOptions();
            options.SkipPresenterTypes.Add("ExplicitlySkippedPresenter");

            Assert.IsTrue(MmvcLiveUiAudit.ShouldSkipPresenter(typeof(SampleSkippedPresenter), options, out string reason1));
            Assert.IsTrue(reason1.Contains("Skipping sample skipped presenter"));

            Assert.IsFalse(MmvcLiveUiAudit.ShouldSkipPresenter(typeof(SampleNormalPresenter), options, out _));
        }

        [Test]
        public void SkipRules_Model_SkipsWhenAttributeOrOptionsMatched() {
            var options = new LiveUiAuditOptions();
            options.SkipModelTypes.Add("ExplicitlySkippedModel");

            Assert.IsTrue(MmvcLiveUiAudit.ShouldSkipModel(typeof(SampleSkippedModel), options, out string reason1));
            Assert.IsTrue(reason1.Contains("Skipping sample skipped model"));

            Assert.IsFalse(MmvcLiveUiAudit.ShouldSkipModel(typeof(SampleNormalModel), options, out _));
        }

        [Test]
        public void SkipRules_Field_SkipsWhenAttributeOrOptionsMatched() {
            var options = new LiveUiAuditOptions();
            options.SkipFields.Add("SampleNormalPresenter.flagToggle");

            var nameField = typeof(SampleNormalPresenter).GetField("nameText");
            Assert.IsTrue(MmvcLiveUiAudit.ShouldSkipField(nameField, typeof(SampleNormalPresenter), typeof(SampleNormalModel), options, out string rName));
            Assert.IsTrue(rName.Contains("Skipped name field"));

            var flagField = typeof(SampleNormalPresenter).GetField("flagToggle");
            Assert.IsTrue(MmvcLiveUiAudit.ShouldSkipField(flagField, typeof(SampleNormalPresenter), typeof(SampleNormalModel), options, out string rFlag));
            Assert.IsTrue(rFlag.Contains("SkipFields"));

            var scoreField = typeof(SampleNormalPresenter).GetField("scoreSlider");
            Assert.IsFalse(MmvcLiveUiAudit.ShouldSkipField(scoreField, typeof(SampleNormalPresenter), typeof(SampleNormalModel), options, out _));
        }
    }
}
