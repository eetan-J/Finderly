# Finderly

A Finder-style file manager and Preview-style image/PDF viewer for Windows, written in C# / WPF (.NET 8).

- Icon, List, Column and Gallery views, tabs, sidebar, path bar, Quick Look (Space), Get Info, tags, undo
- Preview viewer with thumbnails, zoom, rotate and markup
- Software rendering by default; run with `--gpu` to try hardware rendering

Build: `dotnet publish -c Release -r win-x64 --self-contained true -o publish`
Installer: compile `Finderly.iss` with Inno Setup 6.

Original icons; not affiliated with Apple.
