#if UNITY_EDITOR
using GameJam.Gameplay.Flow;
using GameJam.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GameJam.EditorTools
{
    /// <summary>
    /// Authors the bottom tab bar out of the supplied art: the flat blue strip, and per slot the
    /// raised plate that marks the screen the player is on.
    ///
    /// The prefab is the only description of the bar - the scene holds an instance of it and
    /// nothing else - which is the lesson <see cref="GarageScreenBuilder"/> was written for, so
    /// the same shape is used here.
    ///
    /// Two things about re-running it. First, everything new is written only when the thing it
    /// belongs to did not already exist, because the numbers below are a starting point and the
    /// prefab is where the bar is actually tuned. Second, the bar already existed, with the old
    /// layout's positions and sprites on objects that have to survive: the scene's flow holds
    /// references to the three Buttons, so they are renamed and re-dressed rather than replaced.
    /// That re-dressing is a migration, and it runs once - see <c>migrating</c> in
    /// <see cref="Build"/> - so the second run leaves a tuned bar exactly as it found it.
    /// </summary>
    public static class BottomBarBuilder
    {
        private const string MenuTextures = "Assets/GameJam/Textures/UI/MainMenu";

        private const string BarSprite = MenuTextures + "/UI_Bottom.png";
        private const string RaisedSprite = MenuTextures + "/UI_Bottom_Btn.png";
        private const string ShopIconSprite = MenuTextures + "/Btn_Shop.png";
        private const string GarageIconSprite = MenuTextures + "/Btn_Setting_Vehicle.png";
        private const string HomeTexture = MenuTextures + "/Btn_Home.png";

        internal const string PrefabPath = "Assets/GameJam/Prefabs/UI/MainMenu/BottomBar.prefab";
        internal const string RootName = "BottomBar";

        private const string BarName = "Bar";
        private const string ShopSlotName = "ShopSlot";
        private const string HomeSlotName = "HomeSlot";
        private const string GarageSlotName = "GarageSlot";

        private const string RaisedName = "Raised";
        private const string IconName = "Icon";
        private const string LabelName = "Label";

        /// <summary>
        /// What the objects were called before this builder, in the prefab and - for the third
        /// slot, which <see cref="UiBuilder"/> used to rename on the instance - in the scene.
        /// Only ever read, so an old bar can be recognised and renamed into the new one.
        /// </summary>
        private const string OldBarName = "Panel";
        private const string OldShopSlotName = "IapShopButton";
        private const string OldHomeSlotName = "HomeButton";
        private const string OldGarageSlotName = "BulletShopButton";
        private const string OldGarageSlotSceneName = "ShopButton";

        /// <summary>
        /// The mock is 1216 px wide where the canvas is 720, so every pixel below is that art
        /// times 0.592. The bar root is 720x173 (0.135 of the 1280-high reference).
        /// </summary>
        private const float BarHeight = 133f;

        /// <summary>486x286 of plate art at the same scale. Wider than the slot on purpose: the
        /// mock's raised slot spans about 39% of the bar, and a third is 33%.</summary>
        private static readonly Vector2 RaisedSize = new Vector2(288f, 169f);

        private static readonly Vector2 IconSize = new Vector2(64f, 64f);
        private static readonly Vector2 LabelSize = new Vector2(220f, 28f);

        private const float IconRestY = 66f;
        private const float LabelY = 46f;
        private const int LabelFontSize = 22;

        /// <summary>Everything inside a slot hangs off the middle of its bottom edge, which is
        /// the one point that does not move when the slot rises.</summary>
        private static readonly Vector2 SlotBottomCentre = new Vector2(0.5f, 0f);

        /// <summary>The bar sits at the bottom of the screen and stretches across it.</summary>
        private static readonly Vector2 RootAnchorMin = new Vector2(0f, 0f);
        private static readonly Vector2 RootAnchorMax = new Vector2(1f, 0.135f);

        // ------------------------------------------------------------------ Btn_Home

        private const string HomeIconSpriteName = "Btn_Home_Icon";
        private const string HomeLabelSpriteName = "Btn_Home_Label";

        /// <summary>The size the un-cut file is, and the only size the rects below are right for.</summary>
        private const int HomeTextureWidth = 131;
        private const int HomeTextureHeight = 195;

        [MenuItem("Tools/Smashdown/Build Bottom Bar")]
        public static void BuildBottomBar()
        {
            Sprite homeIcon = EnsureHomeIconSprite();
            GameObject prefab = BuildPrefab(homeIcon);
            EnsureSceneInstance(prefab, out Button _, out Button _, out Button _);

            AssetDatabase.SaveAssets();
            Debug.Log(
                "Built the bottom bar into " + PrefabPath + " and pointed the scene's instance at it. "
                + "The three Buttons are the ones they always were, so the flow's references still hold.");
        }

        // ------------------------------------------------------------------ art

        /// <summary>
        /// The house out of Btn_Home, without the word under it.
        ///
        /// The file is the house (top 123 rows), a gap, and then HOME (bottom 42), because the
        /// art was drawn for a bar whose label was part of the icon. The label is TMP text now -
        /// it is only on the raised slot, and it has to say GARAGE and SHOP as well - so the
        /// picture is cut in two here and only the top half is used.
        ///
        /// Cut once. Both guards matter: a sub-sprite already named <see cref="HomeIconSpriteName"/>
        /// means this has run, and a texture that is no longer 131x195 means the file has been
        /// re-exported as the house alone, in which case the rects below would be nonsense and
        /// the whole sprite is already what is wanted.
        /// </summary>
        private static Sprite EnsureHomeIconSprite()
        {
            TextureImporter importer = AssetImporter.GetAtPath(HomeTexture) as TextureImporter;
            if (importer == null)
            {
                Debug.LogWarning(
                    "There is no texture at " + HomeTexture + ", so the home slot is left without "
                    + "an icon.");
                return null;
            }

            bool changed = false;

            // The one UI texture in the project that was still imported as a plain texture. A
            // Default texture has no sprite to load at all, so this comes before the slicing.
            if (importer.textureType != TextureImporterType.Sprite)
            {
                importer.textureType = TextureImporterType.Sprite;
                changed = true;
            }

            if (FindSubSprite(HomeTexture, HomeIconSpriteName) == null && IsUncutHomeTexture())
            {
                importer.spriteImportMode = SpriteImportMode.Multiple;

                // The legacy spritesheet API rather than ISpriteEditorDataProvider: this is two
                // fixed rects written once in the editor, and the data provider costs a package
                // dependency and thirty lines to say the same thing.
#pragma warning disable 618
                importer.spritesheet = new[]
                {
                    // Unity's rects are bottom-left origin, so the house - the top of the PNG -
                    // is the high y.
                    Slice(HomeIconSpriteName, new Rect(0f, 72f, 131f, 123f)),
                    Slice(HomeLabelSpriteName, new Rect(0f, 0f, 131f, 42f)),
                };
#pragma warning restore 618

                changed = true;
            }

            if (changed)
            {
                importer.SaveAndReimport();
            }

            // The cut sprite when there is one, and the whole file when there is not, which is
            // the re-exported house-only case and any future one where the file is already right.
            Sprite icon = FindSubSprite(HomeTexture, HomeIconSpriteName);
            return icon != null ? icon : AssetDatabase.LoadAssetAtPath<Sprite>(HomeTexture);
        }

        private static bool IsUncutHomeTexture()
        {
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(HomeTexture);
            return texture != null
                   && texture.width == HomeTextureWidth
                   && texture.height == HomeTextureHeight;
        }

#pragma warning disable 618
        private static SpriteMetaData Slice(string name, Rect rect)
        {
            return new SpriteMetaData
            {
                name = name,
                rect = rect,
                alignment = (int)SpriteAlignment.Center,
                pivot = new Vector2(0.5f, 0.5f),
                border = Vector4.zero,
            };
        }
#pragma warning restore 618

        /// <summary>
        /// One of a texture's cut sprites by name. A multiple-mode texture's sprites are
        /// sub-assets, so the plain load of the path returns the texture and not any of them.
        /// </summary>
        private static Sprite FindSubSprite(string path, string name)
        {
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(path);
            for (int i = 0; i < assets.Length; i++)
            {
                if (assets[i] is Sprite sprite && sprite.name == name)
                {
                    return sprite;
                }
            }

            return null;
        }

        // ------------------------------------------------------------------ prefab

        private static GameObject BuildPrefab(Sprite homeIcon)
        {
            bool exists = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null;

            GameObject root = exists
                ? PrefabUtility.LoadPrefabContents(PrefabPath)
                : new GameObject(RootName, typeof(RectTransform));

            try
            {
                Build(root, homeIcon);
                return PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                if (exists)
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
                else
                {
                    Object.DestroyImmediate(root);
                }
            }
        }

        private static void Build(GameObject root, Sprite homeIcon)
        {
            RectTransform rect = (RectTransform)root.transform;

            // The whole re-skin, once. The old bar was three icon buttons laid out by hand over a
            // panel sprite, all of it at positions this file is about to contradict, so those
            // positions and sprites have to be written rather than filled in. Doing that on every
            // run is what the garage builder was rewritten to stop: the prefab is where the bar
            // gets tuned afterwards, and a builder that re-wrote it would cost the tuning.
            //
            // The view is the flag because it is the one thing the old bar cannot have had.
            bool migrating = root.GetComponent<BottomBarView>() == null;

            if (migrating)
            {
                // Renamed, not recreated: the Buttons on these objects are the components the
                // scene's GameFlowController holds references to.
                RenameIfPresent(rect, OldBarName, BarName);
                RenameIfPresent(rect, OldShopSlotName, ShopSlotName);
                RenameIfPresent(rect, OldHomeSlotName, HomeSlotName);
                RenameIfPresent(rect, OldGarageSlotName, GarageSlotName);
                RenameIfPresent(rect, OldGarageSlotSceneName, GarageSlotName);

                Place(rect, RootAnchorMin, RootAnchorMax);
            }

            RectTransform bar = BuildBar(rect, migrating);

            RectTransform shopSlot = BuildSlot(rect, ShopSlotName, 0, "SHOP",
                UiBuilder.LoadSprite(ShopIconSprite), migrating);
            RectTransform homeSlot = BuildSlot(rect, HomeSlotName, 1, "HOME", homeIcon, migrating);
            RectTransform garageSlot = BuildSlot(rect, GarageSlotName, 2, "GARAGE",
                UiBuilder.LoadSprite(GarageIconSprite), migrating);

            if (migrating)
            {
                // The bar paints first and the slots over it, so a raised plate covers the bar's
                // top edge the way the mock draws it.
                bar.SetSiblingIndex(0);
                shopSlot.SetSiblingIndex(1);
                homeSlot.SetSiblingIndex(2);
                garageSlot.SetSiblingIndex(3);
            }

            BottomBarView view = UiBuilder.Ensure<BottomBarView>(root);
            SerializedObject serialized = new SerializedObject(view);
            SerializedProperty slots = serialized.FindProperty("slots");

            if (slots != null && slots.arraySize == 0)
            {
                slots.arraySize = 3;
                FillSlot(slots.GetArrayElementAtIndex(0), GameFlowController.GameState.IapShop, shopSlot);
                FillSlot(slots.GetArrayElementAtIndex(1), GameFlowController.GameState.MainMenu, homeSlot);

                // The wrench is the garage, which the flow still calls the shop.
                FillSlot(slots.GetArrayElementAtIndex(2), GameFlowController.GameState.Shop, garageSlot);
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// The flat strip. Bottom-anchored at a fixed height and stretched across the root, so a
        /// taller phone leaves the bar the aspect its art was drawn at and gives the space above
        /// it to the screen rather than to the strip.
        /// </summary>
        private static RectTransform BuildBar(RectTransform root, bool migrating)
        {
            RectTransform bar = UiBuilder.EnsureSpriteImage(BarName, root, BarSprite, Vector2.zero, Vector2.one);

            if (!migrating)
            {
                return bar;
            }

            bar.anchorMin = new Vector2(0f, 0f);
            bar.anchorMax = new Vector2(1f, 0f);
            bar.pivot = new Vector2(0.5f, 0f);
            bar.anchoredPosition = Vector2.zero;
            bar.sizeDelta = new Vector2(0f, BarHeight);

            Image image = UiBuilder.Ensure<Image>(bar.gameObject);
            image.sprite = UiBuilder.LoadSprite(BarSprite);
            image.type = Image.Type.Simple;
            image.preserveAspect = false;
            image.color = Color.white;

            // Off: the bar is decoration, and the three slots over it are the whole of what a tap
            // can land on. On the old panel this was already off; it is written anyway, because
            // migrating has to leave the object in a known state whatever it was in.
            image.raycastTarget = false;
            image.enabled = image.sprite != null;

            return bar;
        }

        /// <summary>
        /// One third of the bar. The slot itself is the tap target and nothing else - a clear
        /// image over the whole third, including the space the raised plate grows into, so the
        /// area a finger has to find does not change when the slot rises.
        /// </summary>
        private static RectTransform BuildSlot(
            RectTransform root,
            string name,
            int index,
            string caption,
            Sprite icon,
            bool migrating)
        {
            Vector2 anchorMin = new Vector2(index / 3f, 0f);
            Vector2 anchorMax = new Vector2((index + 1) / 3f, 1f);

            RectTransform slot = UiBuilder.EnsureRect(name, root, anchorMin, anchorMax);
            Image hit = UiBuilder.Ensure<Image>(slot.gameObject);

            if (migrating)
            {
                Place(slot, anchorMin, anchorMax);

                // The icon has moved to a child, so what is left here is only the hit area. Made
                // clear rather than switched off: a disabled Graphic receives nothing, and an
                // enabled one with no sprite would paint a white third of the bar.
                hit.sprite = null;
                hit.color = new Color(1f, 1f, 1f, 0f);
                hit.type = Image.Type.Simple;
                hit.preserveAspect = false;
                hit.raycastTarget = true;
                hit.enabled = true;
            }

            Button button = slot.GetComponent<Button>();
            if (button == null)
            {
                // Only on a bar built from nothing. The three that matter already exist, and
                // replacing one would break the reference the scene's flow holds to it.
                button = UiBuilder.Ensure<Button>(slot.gameObject);
                button.targetGraphic = hit;
            }

            RectTransform raised = EnsureImage(RaisedName, slot, RaisedSprite, out bool raisedCreated);
            if (raisedCreated)
            {
                PlaceAtSlotBottom(raised, RaisedSize, new Vector2(0.5f, 0f), 0f);

                // Off in the prefab: on the bar, exactly one plate is up at a time and the view
                // decides which, so shipping them all up would show three raised slots for the
                // one frame before it runs.
                raised.gameObject.SetActive(false);
            }

            RectTransform iconRect = EnsureIcon(IconName, slot, icon, out bool iconCreated);
            if (iconCreated)
            {
                PlaceAtSlotBottom(iconRect, IconSize, new Vector2(0.5f, 0.5f), IconRestY);
            }

            TMP_Text label = EnsureLabel(LabelName, slot, caption, out bool labelCreated);
            if (labelCreated)
            {
                PlaceAtSlotBottom((RectTransform)label.transform, LabelSize, new Vector2(0.5f, 0.5f), LabelY);
                label.fontStyle = FontStyles.Bold;
                label.raycastTarget = false;

                // Off in the prefab, for the same reason as the plate: only the raised slot is
                // named, and the view is what decides which that is.
                label.gameObject.SetActive(false);
            }

            return slot;
        }

        private static void FillSlot(SerializedProperty entry, GameFlowController.GameState state, RectTransform slot)
        {
            Transform raised = slot.Find(RaisedName);
            Transform icon = slot.Find(IconName);
            Transform label = slot.Find(LabelName);

            entry.FindPropertyRelative("state").intValue = (int)state;
            entry.FindPropertyRelative("raised").objectReferenceValue = raised != null ? raised.gameObject : null;
            entry.FindPropertyRelative("icon").objectReferenceValue = icon as RectTransform;
            entry.FindPropertyRelative("label").objectReferenceValue = label != null ? label.gameObject : null;
        }

        // ------------------------------------------------------------------ scene

        /// <summary>
        /// Makes sure the scene holds an instance of the bar and that it and the flow can see
        /// each other, and hands back the three Buttons. The shipped scene already has all of
        /// this, so on it only the view's flow reference is new; the rest is what a fresh scene
        /// needs, and is why <see cref="UiBuilder"/> can now call this instead of laying the bar
        /// out itself.
        /// </summary>
        internal static GameObject EnsureSceneInstance(
            GameObject prefab,
            out Button shopButton,
            out Button homeButton,
            out Button garageButton)
        {
            shopButton = null;
            homeButton = null;
            garageButton = null;

            if (prefab == null)
            {
                return null;
            }

            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
            {
                Debug.LogWarning("The bottom bar prefab was built, but there is no loaded scene to put it in.");
                return null;
            }

            Canvas canvas = Object.FindFirstObjectByType<Canvas>(FindObjectsInactive.Include);
            if (canvas == null)
            {
                Debug.LogWarning("The bottom bar prefab was built, but the scene has no Canvas to put it under.");
                return null;
            }

            Transform existing = canvas.transform.Find(RootName);
            GameObject instance;

            if (existing != null)
            {
                instance = existing.gameObject;
            }
            else
            {
                instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, canvas.transform);
                instance.name = RootName;

                // Only on an instance this run put there. One already in the scene may have been
                // moved on purpose, and the flow switches it on and off by itself either way.
                Place((RectTransform)instance.transform, RootAnchorMin, RootAnchorMax);
            }

            ClearStaleNameOverrides(instance);

            shopButton = SlotButton(instance, ShopSlotName);
            homeButton = SlotButton(instance, HomeSlotName);
            garageButton = SlotButton(instance, GarageSlotName);

            BottomBarView view = instance.GetComponent<BottomBarView>();
            GameFlowController flow = Object.FindFirstObjectByType<GameFlowController>(FindObjectsInactive.Include);

            if (view != null && flow != null)
            {
                // The one reference the prefab cannot hold, since it points at a scene object.
                SerializedObject serialized = new SerializedObject(view);
                UiBuilder.SetIfEmpty(serialized, "flow", flow);
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            if (flow != null)
            {
                SerializedObject serialized = new SerializedObject(flow);
                UiBuilder.SetIfEmpty(serialized, "bottomBarRoot", instance);
                UiBuilder.SetIfEmpty(serialized, "iapShopButton", shopButton);
                UiBuilder.SetIfEmpty(serialized, "homeButton", homeButton);
                UiBuilder.SetIfEmpty(serialized, "shopButton", garageButton);
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            else
            {
                Debug.LogWarning(
                    "The bottom bar is in the scene but there is no GameFlowController to wire it to, "
                    + "so nothing will raise a slot.");
            }

            EditorSceneManager.MarkSceneDirty(scene);
            return instance;
        }

        private static Button SlotButton(GameObject instance, string slotName)
        {
            Transform slot = instance.transform.Find(slotName);
            return slot != null ? slot.GetComponent<Button>() : null;
        }

        /// <summary>
        /// Drops a name the scene was overriding the prefab's with.
        ///
        /// Not in the brief, and here because of what the scene turned out to hold: the old
        /// builder renamed the third slot on the instance rather than in the prefab, so the scene
        /// carries an override saying "ShopButton" for an object the prefab now calls GarageSlot.
        /// An override outlives the rename, so without this the hierarchy would show one name and
        /// the prefab another for the rest of the project's life. Nothing breaks either way - the
        /// flow's references are by object, not by name - which is exactly why it would never get
        /// noticed and fixed later.
        ///
        /// Only the names this builder replaced are cleared, so a slot somebody has deliberately
        /// renamed in the scene keeps the name they gave it.
        /// </summary>
        private static void ClearStaleNameOverrides(GameObject instance)
        {
            if (!PrefabUtility.IsPartOfPrefabInstance(instance))
            {
                return;
            }

            Transform[] children = instance.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
            {
                GameObject child = children[i].gameObject;
                if (!IsReplacedName(child.name) || !PrefabUtility.IsPartOfPrefabInstance(child))
                {
                    continue;
                }

                // Compared against the prefab's own name rather than taken on trust, so this
                // reverts something that really is an override and does nothing at all on the
                // second run or on an object the prefab happens to call the same thing.
                GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(child);
                if (source == null || source.name == child.name)
                {
                    continue;
                }

                SerializedObject serialized = new SerializedObject(child);
                SerializedProperty name = serialized.FindProperty("m_Name");
                if (name != null)
                {
                    PrefabUtility.RevertPropertyOverride(name, InteractionMode.AutomatedAction);
                }
            }
        }

        private static bool IsReplacedName(string name)
        {
            return name == OldBarName
                   || name == OldShopSlotName
                   || name == OldHomeSlotName
                   || name == OldGarageSlotName
                   || name == OldGarageSlotSceneName;
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>Renames a child an earlier layout left behind, keeping its components.</summary>
        private static void RenameIfPresent(Transform parent, string from, string to)
        {
            if (parent.Find(to) != null)
            {
                return;
            }

            Transform existing = parent.Find(from);
            if (existing != null)
            {
                existing.name = to;
            }
        }

        /// <summary>Anchors with no offsets, for something that fills its parent or a slice of it.</summary>
        private static void Place(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        /// <summary>
        /// A fixed size hung off the middle of the slot's bottom edge. Everything inside a slot is
        /// placed this way so that its height is the art's own and only its distance up from the
        /// bar changes, which is the whole of the raise.
        /// </summary>
        private static void PlaceAtSlotBottom(RectTransform rect, Vector2 size, Vector2 pivot, float y)
        {
            rect.anchorMin = SlotBottomCentre;
            rect.anchorMax = SlotBottomCentre;
            rect.pivot = pivot;
            rect.sizeDelta = size;
            rect.anchoredPosition = new Vector2(0f, y);
        }

        /// <summary>
        /// Finds or creates a sprite image, and says which it did, so the caller shapes only what
        /// it just made.
        /// </summary>
        private static RectTransform EnsureImage(string name, Transform parent, string spritePath, out bool created)
        {
            Transform found = parent.Find(name);
            created = found == null || found.GetComponent<Image>() == null;
            return UiBuilder.EnsureSpriteImage(name, parent, spritePath, SlotBottomCentre, SlotBottomCentre);
        }

        /// <summary>
        /// The same for the slot's icon, which is handed a sprite rather than a path: the home
        /// icon is one cut out of a bigger texture, and there is no path that loads it.
        /// </summary>
        private static RectTransform EnsureIcon(string name, Transform parent, Sprite sprite, out bool created)
        {
            Transform found = parent.Find(name);
            created = found == null || found.GetComponent<Image>() == null;

            RectTransform rect = UiBuilder.EnsureRect(name, parent, SlotBottomCentre, SlotBottomCentre);
            Image image = UiBuilder.Ensure<Image>(rect.gameObject);

            if (created)
            {
                image.sprite = sprite;
                image.type = Image.Type.Simple;

                // On: the three icons are three different aspects, and a square box would squash
                // the wrench and stretch the store front.
                image.preserveAspect = true;
                image.raycastTarget = false;
                image.enabled = image.sprite != null;
            }

            return rect;
        }

        /// <summary>A label, configured only when it is new, so a size set by hand survives.</summary>
        private static TMP_Text EnsureLabel(string name, Transform parent, string text, out bool created)
        {
            Transform found = parent.Find(name);
            created = found == null || found.GetComponent<TMP_Text>() == null;
            return UiBuilder.EnsureLabel(name, parent, text, LabelFontSize, TextAlignmentOptions.Center,
                SlotBottomCentre, SlotBottomCentre);
        }
    }
}
#endif
