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
                s.Close();
            });
            w.ShowDialog();
        });
    }
}
