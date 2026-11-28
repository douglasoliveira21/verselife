using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.Prototypes.HudDirections
{
    public enum HudState { Explorando, Conversando, Menu }

    /// <summary>One art direction: its own style sheet, fonts, icons and composition. It only draws a moment of the game; it calls nothing.</summary>
    public abstract class HudDirection
    {
        public abstract string Id { get; }
        public abstract string Title { get; }
        protected abstract string Sheet { get; }
        protected abstract string Prefix { get; }
        protected abstract string BodyFont { get; }
        protected abstract void Compose(VisualElement root, HudState state, WorldBackdrop.People people, float pictureAspect);

        public static HudDirection[] All => new HudDirection[] { new HudHorizonte(), new HudMeridiano(), new HudVaranda() };

        /// <summary>The picture of the world with this direction over it, filling whatever holds it.</summary>
        public VisualElement Stage(HudState state, Texture2D picture, WorldBackdrop.People people)
        {
            var stage = new VisualElement(); stage.style.position = Position.Absolute; stage.style.left = 0; stage.style.top = 0; stage.style.right = 0; stage.style.bottom = 0; stage.style.overflow = Overflow.Hidden;
            var back = new Image { image = picture, scaleMode = ScaleMode.ScaleAndCrop }; back.style.position = Position.Absolute; back.style.left = 0; back.style.top = 0; back.style.right = 0; back.style.bottom = 0; stage.Add(back);
            var root = new VisualElement { name = "hud" }; root.AddToClassList("proto"); root.AddToClassList(Prefix); root.style.position = Position.Absolute; root.style.left = 0; root.style.top = 0; root.style.right = 0; root.style.bottom = 0;
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(Kit.Root + "Styles/" + Sheet + ".uss");
            if (sheet != null) root.styleSheets.Add(sheet); else Debug.LogError("[LifeVerse] protótipo: folha de estilo ausente " + Sheet);
            root.style.unityFontDefinition = new StyleFontDefinition(Kit.Font(BodyFont));
            stage.Add(root); Compose(root, state, people, picture != null ? picture.width / (float)picture.height : 16f / 9f); return stage;
        }
    }
}
