# Finderly

A Finder-style file manager and Preview-style image/PDF viewer for Windows, written in C# / WPF (.NET 8).

## Features
- Icon, List, Column and Gallery views, tabs, sidebar, path bar and status bar
- Quick Look (press Space), Get Info, tags, drag and drop, undo
- Preview viewer with thumbnails, zoom, rotate and markup for images and PDFs

## Install
Download **Finderly-Setup.exe** from the [latest release](https://github.com/eetan-J/Finderly/releases/latest) and run it. It includes the .NET runtime, so nothing else is needed. The installer isn't code-signed, so Windows SmartScreen may warn you: choose *More info* then *Run anyway*.

It uses software rendering by default; launch with `--gpu` to try hardware rendering.

## Build
`dotnet publish -c Release -r win-x64 --self-contained true -o publish`, then compile `Finderly.iss` with Inno Setup 6 for the installer.

## Credits
Made with [Claude](https://claude.com/claude-code) (Anthropic) for eetan-J. Original icons; not affiliated with Apple.

## Contributors
- [eetan-J](https://github.com/eetan-J)
- Claude (Anthropic), AI coding assistant
