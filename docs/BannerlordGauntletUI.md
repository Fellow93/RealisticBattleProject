# Bannerlord Gauntlet UI — Reference

A reference for Mount & Blade II: Bannerlord's **Gauntlet** UI system: the widget
catalog, the prefab XML grammar, the Brush/Sprite styling system, the ViewModel
data-binding pipeline, and the Screen/Layer architecture. Written for modders working
in this repo — every claim is drawn from the decompiled TaleWorlds sources
(`decompiled/`), the shipped vanilla GUI assets
(`…/Modules/{Native,SandBox,SandBoxCore,StoryMode}/GUI/`), and RBM's own UI code.

> Assemblies live under `decompiled/`. The pieces:
> - **`TaleWorlds.GauntletUI`** — the widget classes, layout, and Brush model.
> - **`TaleWorlds.GauntletUI.ExtraWidgets`** — fill bars, tooltips, graphs, visibility helpers.
> - **`TaleWorlds.GauntletUI.PrefabSystem`** — the *base* prefab XML engine (widgets, `!Constant`, `*Parameter`). **No data binding.**
> - **`TaleWorlds.GauntletUI.Data`** — the binding extension that adds `@binding`, `{path}`, `DataSource=`, `Command.*` and ties widgets to a `ViewModel`.
> - **`TaleWorlds.Engine.GauntletUI`** — `GauntletLayer`, `UIResourceManager`.
> - **`TaleWorlds.ScreenSystem`** — `ScreenBase`, `ScreenManager`, `ScreenLayer`, `InputRestrictions`.
> - **`TaleWorlds.TwoDimension`** — `Sprite`, `SpriteData`, low-level 2D.
> - **`TaleWorlds.Library`** — `ViewModel`, `MBBindingList<T>`, `[DataSourceProperty]`.

---

## 0. The mental model in one paragraph

A **Screen** (`ScreenBase`) owns one or more **Layers** (`GauntletLayer`). A layer hosts
one or more **Movies** (`GauntletMovie`), and a movie binds one **Prefab** (a widget-tree
`.xml` file) to one **ViewModel** (a C# object). The prefab is parsed into a tree of
**Widgets**; each widget is styled by a **Brush** (which draws named **Sprites** per visual
state). Attributes in the prefab XML wire widget properties to the ViewModel via
**data binding** (`@prop`), scope children onto sub-objects (`DataSource="{child}"`),
and route widget events to VM methods (`Command.Click="ExecuteFoo"`). You show a screen
with `ScreenManager.PushScreen(...)`; you add an overlay with
`layer.LoadMovie("PrefabName", vm)`.

```
ScreenManager  (static stack)
 └─ ScreenBase
     └─ GauntletLayer  (LoadMovie)
         └─ GauntletMovie  = Prefab(.xml)  ⟷  ViewModel(.cs)
             └─ Widget tree  (styled by Brushes → Sprites)
```

---

## 1. Widget catalog

Every widget derives from `Widget` (base `PropertyOwnerObject`, which provides the
data-binding plumbing). A prefab element's **tag name is the widget class name**
(`<ListPanel>` → `TaleWorlds.GauntletUI.BaseTypes.ListPanel`). Any public property is
settable as an XML attribute of the same name.

### Inheritance tree

```
PropertyOwnerObject
└─ Widget
   ├─ BrushWidget
   │  ├─ ImageWidget
   │  │  ├─ TextWidget ─ AnimatedNumberTextWidget, ScrollingTextWidget
   │  │  ├─ ButtonWidget ─ TabToggleWidget
   │  │  ├─ SliderWidget ─ TwoWaySliderWidget
   │  │  ├─ ScrollbarWidget
   │  │  └─ TextureWidget ─ MaskedTextureWidget, OnlineImageTextureWidget
   │  ├─ RichTextWidget ─ ScrollingRichTextWidget
   │  ├─ EditableTextWidget ─ IntegerInputTextWidget (─ IntegerInputPercentageTextWidget), FloatInputTextWidget
   │  ├─ SelectedStateBrushWidget
   │  ├─ FillBar, SmoothDecreaseIndicatorFillBar
   │  ├─ DelayedStateChanger, StateSyncWidget
   │  └─ GraphLinePointWidget
   ├─ Container (abstract)
   │  ├─ BasicContainer
   │  ├─ ListPanel ─ BrushListPanel
   │  └─ GridWidget
   ├─ ScrollablePanel, ScrollablePanelFixedHeaderWidget
   ├─ TabControl
   ├─ DropdownWidget, AnimatedDropdownWidget
   ├─ FillBarWidget, FillBarHorizontalWidget, FillBarVerticalWidget (─ FillBarVerticalClipTierColorsWidget), FillBarVerticalClipWidget
   ├─ TooltipWidget, DialogButtonsParentWidget, InputKeyVisualWidget
   ├─ StringBasedVisibilityWidget, ValueBasedVisibilityWidget, SiblingIndexVisibilityWidget
   ├─ DisabledAlphaChangerWidget, DimensionSyncWidget
   ├─ CircleItemPlacerWidget, CircleActionSelectorWidget
   └─ GraphWidget, GraphLineWidget
```

> **Game-side widgets** modders use constantly — `ImageIdentifierWidget`, `HintWidget`,
> `NavigatableListPanel`, `GameMenuPartyItemButtonWidget`, and ~360 others — live in a
> **separate assembly**, `TaleWorlds.MountAndBlade.GauntletUI.Widgets.dll`
> (namespace `TaleWorlds.MountAndBlade.GauntletUI.Widgets`, plus sub-namespaces
> `.Party`, `.Order`, `.Scoreboard`, `.Tournament`, `.Tutorial`, …). This assembly was
> originally **missing** from `decompiled/` (the Core scope list omitted it) — it has now
> been added, so `decompiled/TaleWorlds.MountAndBlade.GauntletUI.Widgets/` is greppable
> like the rest. The two most-referenced ones are documented below.

### 1a. Base / layout

| Widget | Purpose |
|---|---|
| **Widget** | Root of everything; provides layout, hierarchy, input, state, rendering. Draws a single `Sprite`. |
| **BrushWidget** | Base for anything drawn via a `Brush` (multi-layer, per-state sprites). Owns a `BrushRenderer`, plays brush sounds. |
| **ImageWidget** | `BrushWidget` that auto-switches Default/Hovered/Pressed/Disabled states. Common base for images & buttons. |
| **DimensionSyncWidget** | Copies another widget's width/height (`WidgetToCopyHeightFrom`, `DimensionToSync`, `PaddingAmount`). |

### 1b. Containers / panels

| Widget | Purpose · key props |
|---|---|
| **Container** (abstract) | Selectable child collection + drag/drop. `IntValue` (selected index), `ShowSelection`, `ClearSelectedOnRemoval`. |
| **ListPanel** | The standard stack list. Direction set by nested `StackLayout.LayoutMethod`. `ResetSelectedOnLosingFocus`. |
| **BrushListPanel** | `ListPanel` that also renders its own `Brush` background. |
| **GridWidget** | Grid layout. `RowCount`, `ColumnCount`, `DefaultCellWidth/Height`, `UseDynamicCellWidth/Height`. |
| **ScrollablePanel** | Clip rect + scrollbars + wheel/right-stick scroll. `InnerPanel`, `ClipRect`, `VerticalScrollbar`, `HorizontalScrollbar`, `AutoHideScrollBars`, `FixedHeader`. |
| **TabControl** | Shows exactly one child tab at a time. `SelectedIndex`. Pair with `TabToggleWidget`. |

### 1c. Text

| Widget | Purpose · key props |
|---|---|
| **TextWidget** | Single run of localized plain text. `Text`, `IntText`, `FloatText`, `AutoHideIfEmpty`, `CanBreakWords`. Font/size/color from its `Brush`. |
| **RichTextWidget** | Markup text with inline styles/sprites and clickable links. `Text`, fires `LinkClick`. |
| **EditableTextWidget** | Text input (cursor, selection, clipboard, obfuscation). `Text`, `MaxLength`, `IsObfuscationEnabled`. Fires `TextEntered`. |
| **IntegerInputTextWidget** / **FloatInputTextWidget** | Numeric input with clamp. `IntText`/`FloatText`, `MinInt/MaxInt`, `EnableClamp`. |
| **AnimatedNumberTextWidget** | Counts up to a target. `Number`, `AnimationDuration`. |
| **ScrollingTextWidget** / **ScrollingRichTextWidget** | Marquee on overflow. `IsAutoScrolling`, `ScrollPerTick`. |

### 1d. Buttons / interactive

| Widget | Purpose · key props |
|---|---|
| **ButtonWidget** | Clickable, Default/Hovered/Pressed/Disabled/Selected states. `ButtonType` (Normal/Toggle/Radio), `IsSelected`. Fires `Click`, `DoubleClick`, `AlternateClick`. |
| **TabToggleWidget** | Button that activates a `TabControl` tab. `TabControlWidget`, `TabName`. |
| **SliderWidget** | Draggable value. `ValueFloat/ValueInt`, `MinValue*/MaxValue*`, `Handle`, `Filler`, `IsDiscrete`. |
| **ScrollbarWidget** | Track + handle, wired to a `ScrollablePanel`. `ValueFloat`, `Handle`, `AlignmentAxis`. |
| **DropdownWidget** / **AnimatedDropdownWidget** | Button opening a floating `ListPanel`. `Button`, `ListPanel`, `CurrentSelectedIndex`, `IsOpen`. |
| **DialogButtonsParentWidget** | Wires cancel/confirm/reset buttons + click sounds. |
| **InputKeyVisualWidget** | Shows the sprite for an input key (keyboard/controller aware). `KeyID`, `IconsPath`. |

### 1e. Image / texture

| Widget | Purpose · key props |
|---|---|
| **SelectedStateBrushWidget** | Brush state driven by a bound `SelectedState` string. |
| **TextureWidget** | Renders a dynamic `Texture` via a named `TextureProviderName` (tableaus, scene renders). |
| **MaskedTextureWidget** | Overlays a provider texture (banners) onto the brush. `ImageId`, `AdditionalArgs`, `IsBig`. |
| **OnlineImageTextureWidget** | Loads an image from `OnlineImageSourceUrl`. |

### 1f. Bars / progress (all in ExtraWidgets)

| Widget | Purpose |
|---|---|
| **FillBar** | Brush-layer-driven fill (uses `DefaultFill`/`ChangeFill` layers). `CurrentAmount`/`MaxAmount`/`InitialAmount`, `IsVertical`. |
| **FillBarWidget** | Fill driven by resizing child widgets. `FillWidget`, `ChangeWidget`, `DividerWidget`, `ShowNegativeChange`. |
| **FillBarVerticalWidget** / **FillBarHorizontalWidget** | Directional variants. `IsDirectionUpward`/`IsDirectionRightward`. (RBM's spoils bar is a `FillBarVerticalWidget`.) |
| **FillBarVerticalClipWidget** / **…ClipTierColorsWidget** | Reveal by clipping; tier-color variant lerps `LowColor`→`MaxedColor`. |
| **SmoothDecreaseIndicatorFillBar** | Trailing "ghost" region on value drop (damage indicator). |

> **Fill bars fill on `InitialAmount`, not `CurrentAmount`.** Set `InitialAmount` (or
> `InitialAmountAsFloat`) to make the bar render at load — a known trap.

### 1g. Special / helpers (ExtraWidgets)

| Widget | Purpose |
|---|---|
| **TooltipWidget** | Self-positioning tooltip that follows/mirrors the mouse. `PositioningType`, `AnimTime`. |
| **DelayedStateChanger** | Applies a `State` to a `TargetWidget` after `Delay`. |
| **StateSyncWidget** | Copies a `SourceWidget`'s state onto a `TargetWidget` each frame. |
| **DisabledAlphaChangerWidget** | Fades toward `DisabledAlpha` when disabled. |
| **StringBasedVisibilityWidget** | Visibility from `FirstString` vs `SecondString` (Equal/NotEqual). |
| **ValueBasedVisibilityWidget** | Visibility from numeric compare (`WatchType`: Equal/BiggerThan/LessThan/…). |
| **SiblingIndexVisibilityWidget** | Visibility from a watched widget's sibling index (Equal/Odd/Even/…). |
| **GraphWidget** / **GraphLineWidget** / **GraphLinePointWidget** | 2-axis plotting. |

### 1h. Game-side widgets — `ImageIdentifierWidget` & `HintWidget` (verified)

Both live in `TaleWorlds.MountAndBlade.GauntletUI.Widgets` (now under
`decompiled/TaleWorlds.MountAndBlade.GauntletUI.Widgets/`). Properties below are read
straight from the decompiled source.

**`ImageIdentifierWidget : TextureWidget`** — displays a game entity's picture (hero,
item, banner, crafting piece, party) by resolving an *image identifier* through a
`TextureProvider`. Its own settable properties:

| Property | Type | Meaning |
|---|---|---|
| `ImageId` | `string` | The identifier key. Setting it releases the old texture, pushes `ImageId` to the provider, re-acquires, and refreshes visibility. Bind via `ImageId="@Id"`. |
| `AdditionalArgs` | `string` | Extra provider args (e.g. banner code / colors). `AdditionalArgs="@AdditionalArgs"`. |
| `IsBig` | `bool` | Requests the high-res variant from the provider. |
| `HideWhenNull` | `bool` | If true, the widget hides itself when `ImageId` is null/empty (else always visible). |

Plus inherited `TextureWidget` props — notably `TextureProviderName` (`ImageId="@Id"
AdditionalArgs="@AdditionalArgs" TextureProviderName="@TextureProviderName"` is the usual
vanilla trio) and `LoadingIconWidget`. The backing VM is a
`TaleWorlds.Core.ViewModelCollection.ImageIdentifiers.ImageIdentifierVM` (exposes `Id`,
`AdditionalArgs`, `ImageTypeCode`), bound via `DataSource="{ImageIdentifier}"`.

**`HintWidget : Widget`** — a tooltip trigger. It has **no settable properties of its
own.** It works by listening to its **parent widget's** events: when the parent fires
`HoverBegin` / `HoverEnd` / `DragHoverBegin` / `DragHoverEnd` (and this widget `IsVisible`),
it re-fires the same event on itself, which the bound VM handles. It also blocks preview
mouse/drag interaction (press/release/scroll/drop/dragbegin all return false; only mousemove
passes). Canonical usage:

```xml
<HintWidget DataSource="{DefaultHint}"
            Command.HoverBegin="ExecuteBeginHint" Command.HoverEnd="ExecuteEndHint"
            IsEnabled="false" />
```

Two consequences to remember (they bite in practice):
- The hint reacts to the **parent's** hover, so place the `HintWidget` as a child of the
  widget whose hover should trigger it. `DoNotAcceptEvents` on the parent does **not** stop
  the child from seeing those events.
- The tooltip is a **one-shot snapshot** taken on `HoverBegin`; to live-update its text you
  must replay hide/show. (See memories `gauntlet-hintwidget-tooltips`, `gauntlet-tooltip-refresh`.)

> To verify any other game-side widget's properties, grep
> `decompiled/TaleWorlds.MountAndBlade.GauntletUI.Widgets/` for `class <Name>Widget`.

---

## 2. Widget attributes (the base `Widget` surface)

These apply to **every** widget. Attribute name == C# property name.

### Sizing

| Attribute | Type | Meaning |
|---|---|---|
| `WidthSizePolicy` / `HeightSizePolicy` | `SizePolicy` | How the axis size is computed. |
| `SuggestedWidth` / `SuggestedHeight` | `float` | Desired size (unscaled UI units); used directly when policy is `Fixed`. |
| `MinWidth`/`MaxWidth`/`MinHeight`/`MaxHeight` | `float` | Clamp on measured size. |
| `UseSpriteDimensions` | `bool` | Force `Fixed` + set size from the sprite's pixel size. |

**`SizePolicy`**: `Fixed` (use SuggestedW/H) · `StretchToParent` (fill parent minus margins; in a stack, share leftover by stretch ratio) · `CoverChildren` (shrink-wrap children).

### Position / margins / alignment

| Attribute | Type | Meaning |
|---|---|---|
| `MarginTop/Bottom/Left/Right` | `float` | Outer spacing. |
| `PositionXOffset` / `PositionYOffset` | `float` | Post-layout translation from the computed slot. |
| `HorizontalAlignment` | enum | `Left` · `Center` · `Right`. |
| `VerticalAlignment` | enum | `Top` · `Center` · `Bottom`. |

### Visibility / enable / input

| Attribute | Type | Meaning |
|---|---|---|
| `IsVisible` / `IsHidden` | `bool` | Hidden widgets measure to zero and don't render. |
| `IsEnabled` / `IsDisabled` | `bool` | Drives brush `Disabled` state. |
| `DoNotAcceptEvents` / `CanAcceptEvents` | `bool` | Whether the widget itself takes mouse/input. **Note:** does NOT block children — set `DoNotPassEventsToChildren` for that. |
| `DoNotPassEventsToChildren` | `bool` | Blocks propagation into children. |
| `IsFocusable` | `bool` | Can receive focus. |
| `Id` | `string` | Name for `FindChild` and relative wiring paths (`..\Sibling\Child`). |
| `Tag` | `object` | Arbitrary user data. |

### Clipping / render

`ClipContents`, `ClipHorizontalContent`, `ClipVerticalContent`, `CircularClipEnabled`
+ `CircularClipRadius`, `RenderLate` (draw on top), `DisableRender`.

### Appearance / transform

`Color`, `ColorFactor`, `AlphaFactor`, `SaturationFactor`, `ValueFactor`, `Sprite`,
`ImageFit`, `Rotation`, `PivotX`/`PivotY`, `VerticalFlip`/`HorizontalFlip`,
`NinePatchTop/Bottom/Left/Right`, `ExtendLeft/Right/Top/Bottom`.

### Gamepad / drag

`AcceptNavigation` / `DoNotAcceptNavigation`, `GamepadNavigationIndex`,
`UsedNavigationMovements` (`GamepadNavigationTypes` flags: `Up=1,Down=2,Vertical=3,Left=4,Right=8,Horizontal=12`);
`AcceptDrag`, `AcceptDrop`, `HideOnDrag`, `DragWidget`.

### ListPanel layout — `StackLayout.LayoutMethod`

Written as an attached property on a `ListPanel`. **`LayoutMethod`** values:
`HorizontalLeftToRight` · `HorizontalRightToLeft` · `HorizontalCentered` · `HorizontalSpaced` ·
`VerticalTopToBottom` · `VerticalBottomToTop` · `VerticalCentered` · `VerticalSpaced`.

Non-stretch children contribute `MeasuredSize + margins`; `StretchToParent` children split
leftover space by `ContainerItemDescription.WidthStretchRatio`/`HeightStretchRatio`
(default 1.0). `Spaced` distributes leftover as even gaps; `Centered` centers the block.

---

## 3. Prefab XML grammar

### 3a. File anatomy

```xml
<Prefab>
  <Parameters>          <!-- optional: caller-overridable inputs, each with DefaultValue -->
  <Constants>           <!-- optional: named literal / brush-measured / sprite-measured values -->
  <VisualDefinitions>   <!-- optional: per-state layout animations (VisualState) -->
  <CustomElements>      <!-- optional: reusable inline fragments -->
  <Window>              <!-- REQUIRED: contains exactly ONE root widget -->
    <Widget Id="Root" ...>
      <Children>        <!-- children go inside an explicit <Children> element, NOT as siblings -->
        <TextWidget .../>
        <ListPanel ...><Children>...</Children></ListPanel>
      </Children>
    </Widget>
  </Window>
</Prefab>
```

- `<Window>` must hold exactly one root widget; everything nests under it via `<Children>`.
- **The element tag name is the widget type.** Builtin types resolve from
  `WidgetInfo.GetWidgetInfos()` (every `Widget` subclass in loaded assemblies). An unknown
  name that matches another prefab *file* is spliced in as a **custom type** (§3d).
- There is **no `Type="ListPanel"` attribute** in this codebase — write `<ListPanel>`.

### 3b. The five attribute-value sigils

The whole grammar is decided by the value's leading character:

| Syntax | Kind | Meaning |
|---|---|---|
| `Text="hello"` | **literal** | Parsed and set straight onto the property (enums accept the member name: `WidthSizePolicy="StretchToParent"`). |
| `Text="@Name"` | **binding** | Bind the property to VM property `Name` (relative to current DataSource). |
| `DataSource="{Items}"` | **binding path** | A VM navigation *path* — used where the engine expects a target (a widget's `DataSource`, or a `Command`/`HintWidget` target). Uses `\` separators and `..` to go up: `{..\Items}`. |
| `SuggestedWidth="!Foo.Width"` | **constant** | Resolve from `<Constants>`. |
| `Text="*Title"` | **parameter** | Fill from `Parameter.Title=` at the call site / the `<Parameters>` default. |

**`@` vs `{}`:** `@` marks a data-bound *widget attribute* (a scalar/leaf property that
should track a VM value). `{}` marks a *path* used to re-root a scope — a widget's
`DataSource`, or a command/hint target. Inside a `<Children>`/`<ItemTemplate>` scope,
`@Name` resolves against whatever `DataSource` is in effect.

### 3c. `<Constants>`

```xml
<Constants>
  <Constant Name="RBML.Prod" Value="150" />
  <!-- measure a brush layer so layout tracks the art: -->
  <Constant Name="Btn.Width" BrushName="Standard.PopupCloseButton" BrushLayer="Default" BrushValueType="Width"/>
  <!-- measure a sprite: -->
  <Constant Name="Icon.W" SpriteName="General\foo" SpriteValueType="Width"/>
  <!-- pick from a bound bool: -->
  <Constant Name="Row.Margin" BooleanCheck="{IsSelected}" OnTrue="10" OnFalse="0"/>
</Constants>
```

Modifiers on any constant: `Prefix`, `Suffix`, `Additive` (may be negative — used to
*shrink*), `MultiplyResult`. Reference elsewhere with `!Name`.

### 3d. Custom types, parameters, and `<LogicalChildrenLocation/>`

An element whose tag matches another prefab **file** name splices that prefab in. Push
values into its `<Parameters>` with the `Parameter.` prefix:

```xml
<Standard.TopPanel Parameter.Title="@TitleText" />
<Standard.PopupCloseButton Parameter.ButtonText="@CloseText" Parameter.ButtonAction="ExecuteClose" />
```

Inside the referenced prefab, widgets consume the parameter with `*`:

```xml
<Prefab>
  <Parameters>
    <Parameter Name="Title" DefaultValue="Title Goes Here"/>
    <Parameter Name="Brush" DefaultValue="Frame1Brush"/>
  </Parameters>
  <Window>
    <BrushWidget Brush="*Brush">
      <Children>
        <Widget><LogicalChildrenLocation /></Widget>   <!-- caller's children land here -->
        <TextWidget Text="*Title"/>
      </Children>
    </BrushWidget>
  </Window>
</Prefab>
```

A passed parameter may itself be a binding (`@TitleText`) or a command name
(`ExecuteClose`) — the binding extension re-resolves parameter-typed attributes into
bindings/commands in the child prefab.

### 3e. Lists — `DataSource="{...}"` + `<ItemTemplate>`

```xml
<ListPanel Id="TabStrip" DataSource="{Tabs}" StackLayout.LayoutMethod="VerticalTopToBottom">
  <ItemTemplate>                                  <!-- stamped once per list element -->
    <ButtonWidget Command.Click="ExecuteSelect"   <!-- resolves against the ITEM VM -->
                  IsSelected="@IsSelected">
      <Children>
        <TextWidget Text="@Name"/>
      </Children>
    </ButtonWidget>
  </ItemTemplate>
</ListPanel>
```

- `DataSource="{Tabs}"` binds the panel to an `MBBindingList<T>` property. Inside
  `<ItemTemplate>` the DataSource **is the item VM**, so `@Name`, `@IsSelected`,
  `Command.Click` all resolve against the item type.
- Nested lists nest scopes: `{TownGroups}` → item `{Villages}` → item `{History}`.
- `<ItemTemplate Type="First">` / `Type="Last"` provide edge variants.
- An `<ItemTemplate>` may just reference another prefab: `<ItemTemplate><RecruitTroopPanel/></ItemTemplate>`.
- List mutations flow `MBBindingList` → `ListChanged` → the view adds/removes child views.

### 3f. Commands — `Command.<Event>`

```xml
<ButtonWidget Command.Click="ExecuteSelect"/>
<HintWidget DataSource="{ProductionHint}"
            Command.HoverBegin="ExecuteBeginHint" Command.HoverEnd="ExecuteEndHint" IsEnabled="false"/>
```

`Command.<Event>="MethodName"` binds a widget event to a VM method invoked by reflection
(`ViewModel.ExecuteCommand`) — the method can be `public` or `private`, found by name up
the base chain. String args auto-convert to the method's parameter types.
`CommandParameter.<Event>="7"` supplies an argument (paired by event name).

Common events: `Click`, `DoubleClick`, `AlternateClick` (right-click), `HoverBegin`,
`HoverEnd`. Widgets also fire `ItemAdd`/`ItemRemove`, containers `SelectedItemChange`/`Drop`.

There is **no separate gamepad binding syntax** — gamepad support is which widget events
fire. RBM drives its ledger from a raw polled hotkey in C# (`RBMLedgerHotkey`,
Ctrl+Shift+K), not a prefab binding.

---

## 4. Brush & Sprite system

### 4a. What a Brush is

A **Brush** is a named, reusable bundle of styling — colors, font, one or more sprite
**layers**, per-**state** variants, sounds, and animations. In a prefab, `Brush="Name"` on
a `BrushWidget`-derived widget assigns it. Assigning **clones** the shared brush so
per-widget tweaks don't mutate the original.

```
Brush
 ├─ Layers   : BrushLayer[]   (sprite + color/transform templates; a "Default" layer always exists)
 ├─ Styles   : Style[]        (one per visual STATE; each may re-specify layers by name)
 ├─ Animations
 └─ SoundProperties
```

- **BrushLayer** = one visual layer (a sprite + color/HSV/offset/flip/nine-patch). Multiple
  layers stack (base + border + glow) and composite.
- **Style** = the properties for one **state**. Holds text props (font, color, glow…) and a
  `StyleLayer` per brush layer. Non-default styles fall back to `Default` for anything not
  overridden.
- **Conventional state names** (strings, not an enum): `Default`, `Hovered`, `Pressed`,
  `Selected`, `Disabled`. `BrushRenderer` cross-fades between them (`TransitionDuration`,
  default 0.05s).

### 4b. Brush XML

Loaded by `BrushFactory` from `GUI/Brushes/*.xml` (`Base.xml` first). `Color` values are
`#RRGGBBAA`. `Sprite="…"` resolves via `SpriteData.GetSprite(name)`; `Font` via `FontFactory`.

```xml
<Brushes>
  <Brush Name="Standard.PopupCloseButton">
    <Layers>
      <BrushLayer Name="Default" Sprite="StdAssets\standart_popup_button" />
    </Layers>
    <Styles>
      <Style Name="Default"> <BrushLayer Name="Default" ColorFactor="1.0" Sprite="StdAssets\standart_popup_button" />       </Style>
      <Style Name="Hovered"> <BrushLayer Name="Default" ColorFactor="1.0" Sprite="StdAssets\standart_popup_button_hover" /> </Style>
      <Style Name="Pressed"> <BrushLayer Name="Default" ColorFactor="0.7" Sprite="StdAssets\standart_popup_button" />       </Style>
      <Style Name="Disabled"><BrushLayer Name="Default" ColorFactor="0.2" Sprite="StdAssets\standart_popup_button" />       </Style>
    </Styles>
  </Brush>

  <!-- text brush with a looping alpha pulse -->
  <Brush Name="Popup.ClickToContinue.Text" Font="Galahad" TextHorizontalAlignment="Center">
    <Styles><Style Name="Default" FontColor="#D7AC6FFF" FontSize="35" AnimationToPlayOnBegin="Anim"/></Styles>
    <Animations>
      <Animation Name="Anim" Duration="0.5" Loop="true">
        <AnimationProperty PropertyName="TextAlphaFactor">
          <KeyFrame Time="0.0" Value="1"/><KeyFrame Time="0.25" Value="0.5"/>
        </AnimationProperty>
      </Animation>
    </Animations>
  </Brush>
</Brushes>
```

- `BaseBrush="X"` copies X then applies overrides; `OverrideBrush="X"` mutates an existing
  brush **in place** after all files load (how a later module re-skins a base-game brush).
- **Sounds attach to brushes, not prefabs:**
  `<SoundProperties><EventSounds><EventSound EventName="Click" Audio="panels/next"/></EventSounds></SoundProperties>`.
- `<VisualDefinition>`/`<VisualState>` in the *prefab* (not the brush) animate **layout**
  (offsets, size, margins) per state, as opposed to skin.

### 4c. Sprites

Sprites are referenced **by string name only** (`Sprite="General\Scrollbar…\scroller_bed"`).
The registry is `SpriteData`, loaded from `GUI/SpriteData/*.xml` category files. **Shipped
native modules include no `SpriteData/` folders** — sprites are packed in `.tpac` atlases and
resolved by name globally. RBM ships no sprites of its own; its prefabs/brushes reference
base-game sprite names (`BlankWhiteSquare`, `HintBG@2x_9`, `mission_health_bar_fill_9`, …).

`ImageFit` (on widgets and layers): `Type` = `StretchToFit` (distort) · `Cover` (fill, may
crop) · `Contain` (fit, letterbox), with H/V alignment.

---

## 5. ViewModel data binding (C# side)

### 5a. A bindable ViewModel

```csharp
public class MyVM : ViewModel                     // TaleWorlds.Library.ViewModel
{
    private string _name;
    private MBBindingList<MyItemVM> _items;

    [DataSourceProperty]                          // marker: bindable from XML
    public string Name
    {
        get => _name;
        set { if (value != _name) { _name = value; OnPropertyChangedWithValue(value, "Name"); } }
    }

    [DataSourceProperty]
    public MBBindingList<MyItemVM> Items          // <- DataSource="{Items}" + <ItemTemplate>
    {
        get => _items;
        set { if (value != _items) { _items = value; OnPropertyChangedWithValue(value, "Items"); } }
    }

    private void ExecuteSelect() { ... }          // <- Command.Click="ExecuteSelect"
}
```

- `[DataSourceProperty]` marks a property bindable; `[DataSourceMethod]` marks a method.
  `ViewModel` reflects these once per type into a cached table.
- **Every setter** calls `OnPropertyChangedWithValue(value, "Name")` (the typed overload
  that carries the new value so the view updates without a re-fetch) — or
  `OnPropertyChanged("Name")` for the plain event. This is what makes `@`-bound widgets refresh.
- Collections are `MBBindingList<T>` — a `Collection<T>` that raises `ListChanged` on
  insert/remove/set/clear/sort, so bound `ListPanel`s materialize/destroy item views.
- Command handlers are just named methods; found by name (public or private).
- Nested `{Child}` DataSources are themselves `ViewModel`s. `GetViewModelAtPath` walks
  `\`-separated paths, descending into child VMs and indexing into lists.
- Override `RefreshValues()` to cascade a refresh into list items.

### 5b. Live tooltip / hint refresh gotchas

- A **HintWidget** listens to its **parent's** hover, not its own; `DoNotAcceptEvents`
  doesn't block children (see the `party-expense-tooltip-line` / `gauntlet-hintwidget-tooltips`
  memories).
- Hints are a **one-shot hover snapshot** — to live-update a tooltip's text you must replay
  hide/show on the widget.

---

## 6. Screen / Layer architecture

### 6a. ScreenBase lifecycle

`ScreenBase` owns a sorted `MBList<ScreenLayer>`. Override the `On*` hooks (driven by
internal `Handle*`/`FrameTick` methods):

| Hook | When |
|---|---|
| `OnInitialize()` | Once, on first show. Build layers + VM here. |
| `OnActivate()` / `OnDeactivate()` | Screen (re)gains / loses top-of-stack. |
| `OnReady()` | Once, first active tick. |
| `OnFrameTick(dt)` | Every frame while top. Poll input here (Escape → pop). |
| `OnPostFrameTick(dt)` | After frame tick. |
| `OnFinalize()` | On teardown. `RemoveLayer(...)` + `vm.OnFinalize()`. |

Layer API: `AddLayer` / `RemoveLayer` (re-sorts, refreshes global order),
`FindLayer<T>()`, `SetLayerCategoriesState(...)`.

### 6b. ScreenManager (static stack)

- `PushScreen(screen)` — pauses/deactivates current top, adds + initializes + activates the
  new one. **This is how mods open a screen.**
- `PopScreen()` — finalizes the top, re-activates the one below.
- `TrySetFocus(layer)` / `TryLoseFocus(layer)` — focus goes to the highest-order eligible
  layer (`IsFocusLayer || FocusTest()`). `GauntletLayer` auto-calls `TrySetFocus` when a
  widget gains focus.
- `SortedLayers` = top screen's layers + global layers, sorted by
  `InputRestrictions.Order`. Higher order = on top and gets input first (walked top-down).

### 6c. ScreenLayer / GauntletLayer

- `ScreenLayer(name, localOrder)` — `localOrder` sets `InputRestrictions.Order` (the z /
  input priority). RBM overlays use `-1` (`PlayerArmorStatus`, `RBMLedgerScreen`,
  `RBMConfigScreen`) or `1` (`UnitStatusMissionView`).
- `IsFocusLayer` (bool) — layer always receives keyboard and is a focus candidate.
- `InputRestrictions.SetInputRestrictions(isMouseVisible=true, mask=All)` — makes the layer
  **modal** (capture input + show cursor). `ResetInputRestrictions()` releases.
- **GauntletLayer** is the concrete Gauntlet layer. `LoadMovie(movieName, dataSource)` binds
  a prefab to a VM (a layer can hold several movies). `ReleaseMovie(id)` tears one down.
- **Hotkeys:** read directly from the layer's input context
  (`layer.Input.IsKeyReleased(InputKey.Escape)`), or register a category via
  `layer.Input.RegisterHotKeyCategory(HotKeyManager.GetCategory("GenericPanelGameKeyCategory"))`
  as vanilla screens do.

### 6d. GauntletMovie

`LoadMovie` → `GauntletMovie.Load(...)`. It first tries a **generated (pre-compiled)
prefab** fast path keyed by the VM's full type name; otherwise it parses the prefab XML via
`WidgetFactory.GetCustomType(movieName)`, builds the widget tree + a `GauntletView` tree,
and calls `RefreshBindingWithChildren()`. `Release()` unbinds and detaches.

### 6e. Two ways to put UI on screen

**Full screen** (owns a `ScreenBase`) — e.g. `RBMLedgerScreen`:

```csharp
protected override void OnInitialize()
{
    base.OnInitialize();
    _vm = new MyVM();
    _layer = new GauntletLayer("GauntletLayer", -1);
    _layer.LoadMovie("MyPrefab", _vm);                 // prefab file = GUI/Prefabs/MyPrefab.xml
    _layer.InputRestrictions.SetInputRestrictions();    // modal
    AddLayer(_layer);
    ScreenManager.TrySetFocus(_layer);
}
protected override void OnFrameTick(float dt)
{
    base.OnFrameTick(dt);
    if (_layer.Input.IsKeyReleased(InputKey.Escape)) ScreenManager.PopScreen();
}
protected override void OnFinalize() { RemoveLayer(_layer); _vm.OnFinalize(); base.OnFinalize(); }
// open it:  ScreenManager.PushScreen(new MyScreen());
```

**Mission/overlay HUD** (adds a layer to an existing screen) — e.g. `PlayerArmorStatus`,
`SimulationBattlePanelPatch`: in a `MissionView`/patch, grab the host layer (or
`ScreenManager.TopScreen as MissionScreen`), `new GauntletLayer(name, order)`,
`missionScreen.AddLayer(...)`, `LoadMovie(prefab, vm)`; tick the VM from the view's update;
remove in `OnEndMission`/`OnRemoveBehavior`.

---

## 7. GUI asset registration & discovery

**No `<GUIData>` declaration in SubModule.xml is needed.** `UIResourceManager.Refresh()`
auto-scans every module: if `<Module>/GUI/` exists it's registered. Then:

- **Prefabs** — `WidgetFactory` indexes every `GUI/Prefabs/**/*.xml` by bare filename
  (no extension). That stem is both the `LoadMovie("Name", …)` key **and** the custom-type
  element tag (`<Standard.TopPanel>` ⇒ `Standard.TopPanel.xml`). Duplicate names across
  modules = last-wins (asserts).
- **Brushes** — `BrushFactory` loads `GUI/Brushes/*.xml` (`Base.xml` first).
- **Sprites** — `SpriteData` loads `GUI/SpriteData/*.xml`.
- **Fonts** — `FontFactory`.

Module folder layout to imitate:

```
<Module>/GUI/
  Prefabs/**/*.xml     movie prefabs; filename == LoadMovie() name & custom-type tag
  Brushes/*.xml        brush definitions
  SpriteData/*.xml     sprite category/atlas defs (optional; often none)
  Fonts/               font defs
```

Prefabs/brushes **hot-reload** on file change (`WidgetFactory.PrefabChange` /
`BrushFactory.BrushChange`, which `GauntletMovie` subscribes to).

---

## 8. Injecting into native prefabs (RBM's technique)

RBM never forks a TaleWorlds prefab file. It Harmony-patches the loader and **rewrites the
path** to a patched temp copy. Canonical: `ItemWeightPrefabPatch` (adds an inventory weight
column). Two patches, installed at `OnSubModuleLoad` (Gauntlet caches a prefab after first
parse):

1. **Defeat the codegen fast path** — `Prefix` on
   `GeneratedPrefabContext.InstantiatePrefab`: for the target movie (e.g. `"Inventory"`),
   force `__result = null`, return false, so the tree is parsed from XML (only XML that
   actually gets parsed can be edited).
2. **Redirect the path** — `Prefix(ref string path)` on `WidgetPrefab.LoadFrom`: match by
   filename suffix (`InventoryItemTuple.xml`), register RBM's custom widget type
   (`RBMItemWeightTextWidget.RegisterWidgetType()`), load the original XML, mutate it in
   memory (add `<Constant>`s, insert a cell, shrink a column), save to
   `%TEMP%\RBM\Prefabs\<name>.xml`, and overwrite `ref path`. TaleWorlds' pipeline is
   untouched; no forked file ships.

Sibling patches using the same trick: `SpoilsBarPrefabPatch`, `MaintenanceLabelPrefabPatch`,
`UpgradeLimitPrefabPatch` (all under `RBMCampaign/UI/`). Injected custom widgets are
ordinary `Widget` subclasses registered into `WidgetFactory._builtinTypes` before the
patched XML loads.

See the memory `injecting-into-native-gauntlet-prefabs` for the FillBar-fills-on-InitialAmount
caveat and the `WidgetPrefab.LoadFrom` redirect details.

---

## 9. Conventions worth imitating (from vanilla)

- **Filename == movie/tag name.** Reusable primitives are `Standard.*`. Brush names are
  dotted and mirror scope (`Party.TroopTuple.Extension.TransferButton`).
- **Never hardcode sprite dimensions** — derive with `<Constant BrushName/SpriteName …
  BrushValueType="Width|Height">` and inline via `!Constant`, so layout follows the art.
- **Parameterize `Standard.*`, don't fork.** Pass `*Parameter` values at the call site; use
  `<LogicalChildrenLocation/>` to accept caller children (as `Standard.Window` /
  `Standard.ScrollablePanel` do).
- **Binding discipline:** VMs derive from `ViewModel`, mark bindables `[DataSourceProperty]`,
  use `MBBindingList<T>` for collections, call `OnPropertyChangedWithValue(value,"Name")` in
  every setter.
- **Visuals live in brushes**, not code: provide `Default/Hovered/Pressed/Selected/Disabled`
  styles; sounds via `<SoundProperties>`, motion via `<Animations>`. Use
  `<VisualDefinition>`/`<VisualState>` only when *layout* changes per state.

---

## 10. Where to look

| Need | Location |
|---|---|
| Widget class properties | `decompiled/TaleWorlds.GauntletUI/TaleWorlds.GauntletUI.BaseTypes/` |
| ExtraWidgets (fill bars, tooltips) | `decompiled/TaleWorlds.GauntletUI.ExtraWidgets/…` |
| Game-side widgets (ImageIdentifier, HintWidget, Navigatable*) | `decompiled/TaleWorlds.MountAndBlade.GauntletUI/` |
| Brush / Style / layer model | `decompiled/TaleWorlds.GauntletUI/TaleWorlds.GauntletUI/{Brush,Style,BrushLayer,BrushFactory}.cs` |
| Prefab engine | `decompiled/TaleWorlds.GauntletUI.PrefabSystem/` |
| Binding layer | `decompiled/TaleWorlds.GauntletUI.Data/` |
| Screens / layers | `decompiled/TaleWorlds.ScreenSystem/`, `decompiled/TaleWorlds.Engine.GauntletUI/` |
| ViewModel base | `decompiled/TaleWorlds.Library/…/{ViewModel,MBBindingList,BindingPath}.cs` |
| Real vanilla prefabs | `…/Modules/Native/GUI/Prefabs/Standard/*.xml`, `…/Modules/SandBox/GUI/Prefabs/**/*.xml` |
| Real vanilla brushes | `…/Modules/Native/GUI/Brushes/{Standard,Base,Popup}.xml` |
| Real vanilla VMs | `decompiled/TaleWorlds.CampaignSystem.ViewModelCollection/**`, `decompiled/SandBox.GauntletUI/**` |
| RBM examples | `RBMXML/GUI/Prefabs/` + `RBMXML/GUI/Brushes/` + `RBMCampaign/UI/` |

**Related RBM memories:** `gauntlet-ui-patterns`, `injecting-into-native-gauntlet-prefabs`,
`gauntlet-writeback-widget-object-binding`, `gauntlet-hintwidget-tooltips`,
`gauntlet-tooltip-refresh`, `rbm-ledger-screen`.
