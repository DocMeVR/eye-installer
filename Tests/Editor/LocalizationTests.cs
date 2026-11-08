using DocMe.HideIslands;
using DocMe.HideIslands.Tests;
using NUnit.Framework;

namespace DocMe.EyeInstaller.Tests
{
    public class LocalizationTests
    {
        static string Editor => KeyUse.EditorFolder(L.Table);

        [Test]
        public void EnglishAndJapaneseLoadWithoutProblems()
        {
            var files = KeyUse.Load(L.Table);
            Assert.That(files.Keys, Is.SupersetOf(new[] { "en-us", "ja-jp" }));
            foreach (var pair in files) CollectionAssert.IsEmpty(pair.Value.Problems, pair.Key);
        }

        [Test]
        public void EveryKeyTheCodeUsesExistsInEveryLanguage()
        {
            KeyUse.AssertDefined(KeyUse.Scan(Editor, "L", "GC"), KeyUse.Load(L.Table));
            KeyUse.AssertDefined(KeyUse.Scan(Editor, "HideL", "HGC"), KeyUse.Load(HideIslands.L.Table));
        }

        [Test]
        public void JapaneseMatchesTheCurrentEnglish() => KeyUse.AssertUpToDate(KeyUse.Load(L.Table));

        [Test]
        public void EveryEnglishKeyIsUsed()
            => KeyUse.AssertAllUsed(KeyUse.Scan(Editor, "L", "GC"), KeyUse.Load(L.Table)[DocMeLanguage.English]);

        [Test]
        public void TextFollowsTheChosenLanguage()
        {
            var saved = DocMeLanguage.StoreOverride;
            try
            {
                DocMeLanguage.StoreOverride = new DocMeMemoryLanguageStore("ja-jp");
                Assert.AreEqual("目をインストール", L.Text("nav.install"));
                Assert.AreEqual("クリック 2 回", L.Count(2, "report.clicks", 2));
                DocMeLanguage.StoreOverride = new DocMeMemoryLanguageStore("en-us");
                Assert.AreEqual("Install eyes", L.Text("nav.install"));
                Assert.AreEqual("1 click", L.Count(1, "report.clicks", 1));
                Assert.AreEqual("Placed 2 eye objects.", L.Count(2, "report.placed", 2));
            }
            finally { DocMeLanguage.StoreOverride = saved; }
        }
    }
}
