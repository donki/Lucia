using System.Windows.Controls;
using SocLucia.Localization;
using SocLucia.Services;

namespace SocLucia.Tests;

/// <summary>Las tarjetas de Habilidades y Frameworks en Ajustes: se pintan, con sus textos y el framework REA.</summary>
public class SkillsFrameworksSettingsTests : UiTest
{
    [Fact]
    public void Ajustes_muestra_las_tarjetas_de_habilidades_y_frameworks()
    {
        Ui.Run(() =>
        {
            var w = new SettingsWindow();
            Ui.Expect<SettingsWindow>(s =>
            {
                Assert.Equal(Loc.Get("SkillsTitle"), Ui.Find<TextBlock>(s, "SkillsTitle").Text);
                Assert.Equal(Loc.Get("SkillsNone"), Ui.All<TextBlock>(Ui.Find<StackPanel>(s, "SkillsList")).First().Text);

                Assert.Equal(Loc.Get("FrameworksTitle"), Ui.Find<TextBlock>(s, "FrameworksTitle").Text);
                // El catálogo trae REA: su nombre sale en la lista de frameworks.
                Assert.Contains(Ui.All<TextBlock>(Ui.Find<StackPanel>(s, "FrameworksList")), t => t.Text.Contains("REA"));

                // Modelos en línea: la tarjeta se pinta y lista los proveedores (Groq entre ellos).
                Assert.Equal(Loc.Get("OnlineTitle"), Ui.Find<TextBlock>(s, "OnlineTitle").Text);
                Assert.Contains(Ui.All<TextBlock>(Ui.Find<StackPanel>(s, "OnlineProvidersList")), t => t.Text.Contains("Groq"));
                // Multi-modelo: su tarjeta y las 4 estrategias en el desplegable.
                Assert.Equal(Loc.Get("MultiTitle"), Ui.Find<TextBlock>(s, "MultiTitle").Text);
                Assert.Equal(4, Ui.Find<System.Windows.Controls.ComboBox>(s, "MultiStrategyCombo").Items.Count);

                // Ajustes por pestañas: las 7 con su texto localizado.
                var tabs = Ui.Find<System.Windows.Controls.TabControl>(s, "Tabs");
                Assert.Equal(7, tabs.Items.Count);
                Assert.Equal(Loc.Get("TabFrameworks"), Ui.Find<System.Windows.Controls.TabItem>(s, "TabFrameworks").Header);
                // Cuenta/Sincronización vive en «Más opciones».
                Assert.Equal(Loc.Get("AccountTitle"), Ui.Find<TextBlock>(s, "AccountTitle").Text);

                // El buscador salta a la pestaña del ajuste: «mcp» → Frameworks.
                Ui.Find<TextBox>(s, "SettingsSearchBox").Text = "mcp";
                Ui.Click(Ui.Find<System.Windows.Controls.Primitives.ButtonBase>(s, "SettingsSearchButton"));
                Assert.Same(Ui.Find<System.Windows.Controls.TabItem>(s, "TabFrameworks"), tabs.SelectedItem);
                s.Close();
            });
            w.ShowDialog();
        });
    }
}
