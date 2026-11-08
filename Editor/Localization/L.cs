using DocMe.HideIslands;

namespace DocMe.EyeInstaller
{
    /// <summary>Eye Installer texts in the chosen language, from Editor/Localization/EyeInstaller.xx-XX.po. The language
    /// itself is shared with Hide Mesh Islands (and with NDMF tools when NDMF is installed).</summary>
    public static class L
    {
        public static readonly PoTable Table = new PoTable("EyeInstaller.");

        public static string Text(string key) => Table.Text(key);
        public static string Text(string key, params object[] args) => Table.Text(key, args);
        public static string Count(int n, string key, params object[] args) => Table.Count(n, key, args);
        public static string Tip(string key) => Table.Tip(key);
    }
}
