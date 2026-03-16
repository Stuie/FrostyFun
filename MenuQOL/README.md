# MenuQOL

Menu quality-of-life improvements for Sledding Game Demo.

## Features

- **Auto-confirm host dialog**: Automatically clicks "Confirm" on the internet connection popup when hosting
- **Password field QOL**:
  - Click password field → auto-checks "require password" toggle
  - Check password toggle → auto-focuses password field for typing
  - Press Enter in password field → submits/creates lobby
  - Remembers your last-used password between sessions

## Controls

| Key | Action |
|-----|--------|
| F6 | Toggle auto-confirm host dialog (default: ON) |
| F7 | Dump UI elements to log (development/debugging) |

## Installation

1. Build the mod: `dotnet build -c Release`
2. Copy `bin/Release/net6.0/MenuQOL.dll` to `[Game]/Mods/`
3. Launch the game

## How It Works

### Auto-Confirm Host Dialog

When you click the HOST button, a popup asks you to confirm you have a good internet connection. This mod hooks the HOST button click and automatically confirms the popup when it appears.

### Password QOL

The mod hooks the lobby creation UI elements:
- `(Input) lobby setting password` - TMP_InputField for password
- `(Toggle) uses password` - Toggle checkbox
- `(Button) CONFIRM HOST` - Create lobby button

When the password field is selected, the toggle is auto-checked. When the toggle is checked, the field is auto-focused. The `onSubmit` event of the input field triggers the create button when Enter is pressed. Passwords are persisted using Unity's PlayerPrefs.

## Technical Details

- Uses event-based hooking rather than polling where possible
- Resets state on scene load to handle scene transitions
- F7 UI dump excludes world geometry to focus on actual UI elements
