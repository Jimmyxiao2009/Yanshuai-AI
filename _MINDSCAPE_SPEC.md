# Mindscape 设计语言 — 页面重做规范（Lumina.Roleplay）

You are restyling existing UWP XAML pages of a Chinese AI character-roleplay app to a new design language called **心象 / Mindscape**. The design foundation, theme, and control styles ALREADY EXIST. Your job is layout/visual restyling of specific pages — NOT logic changes.

## 设计语言 (apply consistently)
- Neutral base ("晨雾" light / "夜墨" dark); the ONLY chromatic accent is the 心象色 (a dusk blue-violet `#5E5DB6`, themeable).
- Headers & character names → title font (站酷小薇). Dialogue/voice text → `YanshuaiVoiceFont` (文楷). UI body → body font. Latin/numerals can use the english font (Cormorant serif).
- Calm, generous spacing; soft rounded corners (8–12); subtle borders; one accent per screen.
- Page header pattern: a small accent dot (`<Ellipse Width="9" Height="9" Fill="{ThemeResource YanshuaiAccentBrush}"/>`) + title in title font ~30px.

## HARD RULES (violating these breaks the build — do not)
1. **Preserve EVERY `x:Name`, every event handler attribute (`Click=`, `Tapped=`, `SelectionChanged=`, `KeyDown=`, `ItemClick=`, `Toggled=`, `TextChanged=`, etc.), and every `{Binding ...}` path EXACTLY.** First run a grep on each page to list them, redesign, then verify none were dropped/renamed. Code-behind depends on them.
2. **Do NOT introduce any new `{ThemeResource}` key.** Use ONLY the keys listed below (they exist in all themes). Inventing a key crashes at runtime on non-Mindscape themes.
3. **Edit existing files only. Do NOT create new files** (new .cs/.xaml require csproj entries — out of scope).
4. Visual/layout only — do not change logic, bindings' meaning, navigation, or remove functional elements.
5. After editing each file, validate it is well-formed XML (PowerShell: `[xml](Get-Content -Raw -LiteralPath $p)`). Fix if it throws.
6. Match the surrounding code's comment style (Chinese comments are fine).

## Allowed ThemeResource keys (brushes/fonts — exist in ALL themes)
Brushes: `YanshuaiPageBrush, YanshuaiChromeBrush, YanshuaiAccentBrush, YanshuaiAccentHoverBrush, YanshuaiAccentPressedBrush, YanshuaiOnAccentBrush, YanshuaiPaneHeaderBrush, YanshuaiSurfaceBrush, YanshuaiPanelBrush, YanshuaiSubtlePanelBrush, YanshuaiBorderBrush, YanshuaiTextBrush, YanshuaiMutedTextBrush, YanshuaiShadowBrush, YanshuaiUserBubbleBrush, YanshuaiAiBubbleBrush, YanshuaiAccentWashBrush, YanshuaiMemoryLineBrush`
Fonts: `YanshuaiTitleFont` (站酷小薇), `YanshuaiBodyFont`, `YanshuaiVoiceFont` (文楷), `YanshuaiEnglishFont`
Corner radius (ThemeResource): `YanshuaiControlCornerRadius`

## Allowed StaticResource tokens (from Themes/Base/Tokens.xaml)
Spacing (x:Double): `SpaceXXS SpaceXS SpaceS SpaceM SpaceL SpaceXL SpaceXXL SpaceXXXL`
Thickness: `PagePadding CardPadding ListItemPadding`
Font sizes (x:Double): `FontSizeCaption FontSizeBody FontSizeBodyLarge FontSizeSubtitle FontSizeTitle FontSizeHeader FontSizeDisplay`
Icon/size: `IconSizeSmall IconSizeMedium IconSizeLarge TouchTargetHeight NavItemHeight AvatarSizeS AvatarSizeM AvatarSizeL`
Corner radius: `CardCornerRadius PillCornerRadius BubbleCornerRadius`
Opacity (x:Double): `OpacityFaint(0.35) OpacitySubtle(0.55) OpacityMuted(0.72)`  ← use these instead of hardcoded Opacity magic numbers

## Named styles (from Themes/Base/Controls.xaml & Typography.xaml) — prefer these
Buttons: `PrimaryButtonStyle` (accent fill), `SecondaryButtonStyle` (outline), `AccentOutlineButtonStyle`, `GhostButtonStyle` (transparent, subtle hover), `IconButtonStyle` (square 40px touch target — use for icon-only buttons; set Width/Height as needed), `SendButtonStyle`
Containers (Border): `CardBorderStyle` (panel+border+rounded+padding), `PillBorderStyle`, `ChipBorderStyle`
Icon: `ToolbarIconStyle` (FontIcon)
Text (TextBlock): `DisplayTextStyle`(34) `TitleTextStyle`(22) `SubtitleTextStyle`(18) `BodyTextStyle`(14) `VoiceTextStyle`(文楷 15) `LabelTextStyle`(12 muted, letter-spaced — for section labels) `CaptionTextStyle`(12 muted)

## Common improvements to make (from the app's UI critique)
- Icon-only buttons that were tiny (e.g. 30x28) → use `IconButtonStyle` with Width/Height ≥ 36 for touch.
- Replace scattered hardcoded `Opacity="0.5"` etc. with the opacity tokens.
- Section headers → `LabelTextStyle` (small, muted, letter-spaced) or title-font subheaders.
- Settings rows → consistent: label (body) + description (caption muted) + control on the right; group related rows in a `CardBorderStyle` panel with `SpaceL` gaps.
- Empty states → accent ring circle (84x84, CornerRadius 42, Background `YanshuaiAccentWashBrush`, BorderBrush `YanshuaiAccentBrush`) + icon + muted caption.
- Top bars on sub-pages → title in title font; back/action buttons via `IconButtonStyle`.
- Keep `CommandBar`s as-is structurally (themes adapt them); only adjust if clearly beneficial and safe.

## Reference implementations already done (read these to match the style)
- `Shell/ShellPage.xaml` (nav, accent-wash selection, accent dot header)
- `Pages/Chat/MainPage.xaml` (bubbles, memory-line rail, composer, IconButtonStyle action strip, empty welcome ring)
- `Pages/Characters/CharacterCardsPage.xaml` + `Controls/CharacterCardView.xaml` (page header, card, group markers, empty state)

## Deliverable
For each page: grep its hooks first, redesign preserving all of them, validate XML. Then report: list of files edited, and an explicit confirmation line per file: "hooks preserved: <none dropped>" (or list any you were unsure about).
