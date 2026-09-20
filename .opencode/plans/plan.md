# FluentAvalonia Migration & Responsive Design Plan

## Current State Analysis

**Tech Stack:**
- Avalonia 12.1.2 (FluentTheme) — **not** FluentAvalonia
- .NET 10, C# 13
- Targets: Desktop (WinExe), Android, iOS (deferred), Browser (deferred)
- MVVM: CommunityToolkit.Mvvm 8.4.2

**Existing UI (ataman-assistant/Views):**
- `ShellView.axaml` — TabControl with "Asistan" / "Ayarlar" tabs
- `MainView.axaml` — Chat transcript (ItemsControl), input bar, mic test button, status
- `SettingsView.axaml` — Long ScrollViewer form: language, wake word, TTS voice, model, download, token/temp sliders
- `MainWindow.axaml` — Standard Window (900×600, Min 480×360)
- `App.axaml` — `<FluentTheme />` only

**Gaps vs. FluentAvalonia:**
- No responsive navigation (TabControl breaks on narrow widths)
- No modern controls: NavigationView, InfoBar, SettingsExpander, NumberBox, ToggleSwitch
- No proper conversation UI (message bubbles, streaming indicator)
- No theme-aware styling (hardcoded `#22000000`, Opacity)
- No mobile-friendly breakpoints

---

## Migration Strategy

### Phase 1 — Foundation (NuGet + Theme Setup)

1. **Add FluentAvalonia NuGet** to `ataman-assistant/AtamanAssistant.csproj`:
   ```xml
   <PackageReference Include="FluentAvalonia" Version="1.7.0" />
   ```
   (1.7.x targets Avalonia 12; verify latest pre-release if needed)

2. **Update `App.axaml`** to use FluentAvalonia styling:
   - Remove `<FluentTheme />`
   - Add `<FluentAvaloniaTheme PreferUserAccentColor="True" />`
   - Register `FluentAvalonia` resource dictionaries

3. **Update `Directory.Build.props`** if version pinning needed.

### Phase 2 — Responsive Shell (NavigationView)

**Replace `ShellView.axaml` TabControl with `NavigationView`:**
- **Compact/Minimal modes** for narrow windows (< 640px)
- **Left navigation pane** with icons: "Asistan" (Chat), "Ayarlar" (Settings)
- **Content area** hosts `MainView` / `SettingsView`
- **Back button** handled by NavigationView automatically
- **PaneDisplayMode**: `Auto` (responsive: LeftCompact → LeftMinimal → Top)

**Responsive breakpoints** (via VisualStateManager or custom AttachedProperty):
- ≥ 1024px: LeftExpanded (full labels)
- 640–1023px: LeftCompact (icons only)
- < 640px: LeftMinimal (hamburger only) or Top

### Phase 3 — MainView Redesign (Conversation UI)

**New structure using FluentAvalonia controls:**
```
MainView (UserControl)
├── CommandBar / TitleBar area
│   ├── App title + status (InfoBar for streaming/errors)
│   └── Primary commands: New Chat, Mic toggle
├── Conversation area (ScrollViewer + VirtualizingStackPanel)
│   └── Message bubbles (DataTemplateSelector: User/Assistant/System)
│       ├── User: right-aligned, accent background
│       ├── Assistant: left-aligned, surface background, streaming animation
│       └── System/Status: centered, muted
├── Input area (CommandBar or Grid at bottom)
│   ├── Mic button (ToggleButton with icon states)
│   ├── TextBox (AutoSuggestBox for future commands)
│   └── Send button (hidden when mic active)
```

**Key FluentAvalonia controls:**
- `InfoBar` — for "Thinking…", "Listening…", errors (replaces StatusText Border)
- `PersonPicture` — optional avatars
- `ProgressRing` — inline in assistant bubble while streaming
- `ToggleSwitch` — for mic test (replaces Button)
- `AutoSuggestBox` — for text input (placeholder, query icon)

**Responsive behavior:**
- ≥ 600px: Side-by-side bubbles with comfortable margins
- < 600px: Full-width bubbles, stacked input bar

### Phase 4 — SettingsView Redesign (SettingsExpander + Forms)

**Restructure into categorized sections using `SettingsExpander`:**
```xml
<SettingsExpander Header="Genel" IsExpanded="True">
    <StackPanel Spacing="12">
        <!-- Language, Wake Word -->
    </StackPanel>
</SettingsExpander>

<SettingsExpander Header="Ses ve Yanıt" IsExpanded="True">
    <StackPanel Spacing="12">
        <!-- TTS Voice, SpeakResponses, AlwaysListening -->
    </StackPanel>
</SettingsExpander>

<SettingsExpander Header="Model (GGUF)" IsExpanded="False">
    <StackPanel Spacing="12">
        <!-- Model ComboBox, Download button, ProgressBar, Token/Temp sliders -->
    </StackPanel>
</SettingsExpander>
```

**Control replacements:**
- `ComboBox` → `ComboBox` (FA styled) or `RadioButtons` for few options
- `CheckBox` → `ToggleSwitch`
- `Slider` → `NumberBox` (for tokens) + `Slider` (for temperature)
- `Button` (Download) → `Button` with `ProgressRing` overlay
- `ProgressBar` → `ProgressBar` (FA styled) or `ProgressRing` inline

**Responsive:**
- Stack expanders vertically on all sizes
- Two-column form layout (Label + Control) on ≥ 640px via Grid
- Single-column stacked on mobile

### Phase 5 — Theming & Polish

1. **Theme variants**: Support Light/Dark/System via `RequestedThemeVariant` binding
2. **Accent color**: Bind to system accent (`PreferUserAccentColor="True"`)
3. **Mica/Acrylic** (Desktop only): `Window.SystemBackdrop = new MicaBackdrop()` in `MainWindow` code-behind
4. **Iconography**: Use `SymbolIcon` / `FontIcon` (Segoe Fluent) consistently
5. **Animations**: `ConnectedAnimation` for navigation transitions
6. **Density**: `Resources["ControlDensity"] = ControlDensity.Compact` option in settings

### Phase 6 — Platform-Specific Adjustments

**Desktop (`ataman-assistant.Desktop`):**
- Enable Mica backdrop on Windows 11
- TitleBar customization (`ExtendClientAreaToDecorationsHint`, `TitleBar`)

**Android (`ataman-assistant.Android`):**
- NavigationView Top mode default (mobile pattern)
- Ensure touch-friendly hit targets (≥ 48dp)
- Material3-inspired color scheme via FA theming

**Shared:**
- `ResponsivePanel` helper (AttachedProperty) for breakpoint-driven visibility
- `Breakpoint` enum: `Mobile`, `Tablet`, `Desktop`

---

## File Changes Summary

| File | Action |
|------|--------|
| `ataman-assistant/AtamanAssistant.csproj` | Add `FluentAvalonia` package |
| `ataman-assistant/App.axaml` | Replace FluentTheme → FluentAvaloniaTheme |
| `ataman-assistant/Views/ShellView.axaml` | **Rewrite** with NavigationView |
| `ataman-assistant/Views/MainView.axaml` | **Rewrite** with conversation bubbles, InfoBar, AutoSuggestBox |
| `ataman-assistant/Views/SettingsView.axaml` | **Rewrite** with SettingsExpander, ToggleSwitch, NumberBox |
| `ataman-assistant/Views/MainWindow.axaml` | Add Mica/TitleBar customization (Desktop) |
| `ataman-assistant/ViewModels/MainViewModel.cs` | Add `IsStreaming`, `ConversationMessages` (ObservableCollection<ChatMessageViewModel>) |
| `ataman-assistant/ViewModels/SettingsViewModel.cs` | Minor: expose `ControlDensity` if added |
| `ataman-assistant/ViewModels/ChatMessageViewModel.cs` | **New** — Role, Content, IsStreaming, Timestamp |
| `ataman-assistant/Converters/*.cs` | **New** — BoolToVisibility, RoleToTemplateSelector, etc. |
| `ataman-assistant/Behaviors/ResponsiveBehavior.cs` | **New** — Breakpoint detection via SizeChanged |

---

## Verification Checklist

- [ ] `dotnet build ataman-assistant.Desktop\AtamanAssistant.Desktop.csproj -c Debug` succeeds
- [ ] App launches, NavigationView switches tabs correctly
- [ ] Resize window: NavigationView adapts (Expanded → Compact → Minimal/Top)
- [ ] MainView: messages render as bubbles, streaming shows ProgressRing
- [ ] SettingsView: expanders collapse/expand, toggles/sliders work, download shows progress
- [ ] Theme toggle (Light/Dark/System) works via Settings or system
- [ ] Mobile breakpoint (< 640px): single-column layouts, touch targets ≥ 48px
- [ ] Android build: `dotnet build ataman-assistant.Android\AtamanAssistant.Android.csproj -c Debug -f net10.0-android` succeeds
- [ ] SmokeTest: `dotnet run --project tools\SmokeTest\SmokeTest.csproj -c Debug` exits 0

---

## Estimated Effort

| Phase | Files Touched | Complexity |
|-------|---------------|------------|
| 1. Foundation | 2 | Low |
| 2. Shell | 1 | Medium |
| 3. MainView | 2 (View + VM) + Converters | High |
| 4. SettingsView | 1 | Medium |
| 5. Theming | 2–3 | Medium |
| 6. Platform | 2 | Low–Medium |

**Total: ~6–8 focused sessions** (each phase verifiable independently).

---

## Open Questions for User

1. **FluentAvalonia version**: Target `1.7.x` (Avalonia 12 compatible) or wait for stable `1.8`? Current `1.6.2` targets Avalonia 11 — may need pre-release.
2. **Mica backdrop**: Enable on Desktop only? (Requires Windows 11 22H2+)
3. **Conversation history persistence**: Currently in-memory only. Add JSONL export/import later?
4. **Density setting**: Add `ControlDensity` (Default/Compact) toggle in Settings?
5. **Android navigation default**: Top (mobile) or LeftCompact (tablet)?
6. **Wake word default**: AGENTS.md says `asistan` (Vosk TR dict lacks `ataman`). Confirm Settings default.

---

## Next Steps

1. Confirm FluentAvalonia version choice
2. Start Phase 1 (NuGet + App.axaml)
3. Incrementally replace views, verifying at each phase