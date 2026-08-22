# Third-Party Notices

DesktopPossible incorporates third-party libraries, upstream code, and
media assets. This document provides attribution and license information.

## Upstream Code Lineage

DesktopPossible is a hard fork of **Desktop Frames +** (MIT) by
limbo666 / Nikos Georgousis, which is itself a continuation of **BirdyFences**
(MIT) by HakanKokcu.

- Desktop Frames +: https://github.com/limbo666/DesktopFramesPlus
- BirdyFences by HakanKokcu (original project)

The full MIT copyright notices for both upstream projects are retained in this
repository's [LICENSE](LICENSE) file.

## NAudio

**License:** MIT

Audio playback library used for notification sounds.

- Version: 2.2.1
- Repository: https://github.com/naudio/NAudio
- License: https://github.com/naudio/NAudio/blob/master/license.txt

## Newtonsoft.Json

**License:** MIT

JSON serialization library used for configuration, profiles, and the remote
manifest.

- Version: 13.0.1
- Repository: https://github.com/JamesNK/Newtonsoft.Json
- License: https://github.com/JamesNK/Newtonsoft.Json/blob/master/LICENSE.md

## Bundled Notification Sounds

The notification sounds bundled in `src/DesktopPossible/Resources/*.wav`
were sourced from [Pixabay](https://pixabay.com/) and are used under the
[Pixabay Content License](https://pixabay.com/service/license-summary/), which
permits redistribution of the content as an integrated part of an application.

With thanks to the Pixabay creators whose sounds are included (author names as
embedded in the filenames):

- **UNIVERSFIELD** — multiple notification sounds
- **Dragon Studio** (dragon-studio) — new-notification sound
- **koiroylers** — slow-ding sound
- **SoundShelfStudio** (soundshelfstudio) — UI notification pop
- Additional Pixabay notification and UI sound effects by their respective
  authors

## Windows Script Host / Shell32 (COM)

The application references the Windows Script Host object model
(`IWshRuntimeLibrary`) and the Windows Shell (Shell32) via COM. These are
components of the Microsoft Windows operating system; they are consumed at
runtime from the user's OS installation and are **not redistributed** with this
application.

---

For the complete license text of each dependency, refer to the links above or
the license files included in the respective NuGet packages.
