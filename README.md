# The Vanillafiyator

I made this for myself because I got tired of moving the same Dead as Disco
mods out of the game folder and then forgetting where I put them. It puts
the usual mod folders away and brings them back when I want them again.
Also yes, I know how I spelt it. The name is staying.

This is a **Windows x64 beta**. If you have the beta ZIP, extract it outside
the game folder and double-click `TheVanillafiyator.exe`. The exe brings its
own .NET runtime, so there's nothing else to install.

## Using it

1. On the first run, paste the game folder that contains `Pagoda.exe`.
   In Steam, **Manage > Browse local files** takes you there. It remembers
   that folder for next time.
2. Press **1** to see what it picked up.
3. Close Dead as Disco, then press **2** to put those mods away. You can
   launch the game once the move finishes.
4. Close the game again and press **3** to put the mods back. **0** exits.

> [!IMPORTANT]
> Keep the sibling `Dead as Disco.vanillafiyator` folder while mods are
> isolated. That's where the mods and the restore record live. Restore
> before moving the game to another Steam library.

## What it picks up

I look for the common `~mods`, `LogicMods`, UE4SS and Limelight paths.
Scan flags some other loader files for review. If you've put mods in
custom folders or replaced original game files, those changes can stay
in the install.

If a move stops, **1** shows what made it to storage and **3** can put the
stored items back. If you get a conflict, keep both copies and send the
full error when you [report it](https://github.com/Henreh1/The-Vanillafiyator/issues).
For beta feedback, tell me what sort of mods you use, whether the game
opened after isolating, and whether your mods came back.

<a href="https://brainmade.org/">
<picture>
  <source media="(prefers-color-scheme: dark)" srcset="https://brainmade.org/white-logo.svg">
  <source media="(prefers-color-scheme: light)" srcset="https://brainmade.org/black-logo.svg">
  <img alt="brainmade" src="https://brainmade.org/white-logo.svg">
</picture>
</a>
