# Healing given to teammates

Date: 2026-09-27. Branch: `feature/healing-given`.

## Problem

In co-op nearly every potion can be thrown at a teammate. Some of them heal: Blood Potion, Ambergris, and Fruit Juice, which raises max HP and heals by the same amount. Regen Potion puts Regen on a teammate, which heals them each turn. The recap counts none of it. Defense shows each player's own HP healed, but not who healed them.

## Decision

**HP a player restores to a teammate is support: a new kind, "Healing".**

- **Where it's measured:** every heal goes through the game's `CreatureCmd.Heal`. The amount counted is what the creature actually regained, `min(amount, max HP − current HP)`, the same as the game's own run history. The recipient is the player, or a pet's owner.
- **Who gave it:** the same rules as the other kinds of support (`SupportGiver`), with one addition. A potion being used names its owner as the giver, in or out of combat, because potions can be thrown on the map too. The potion's use is watched on `PotionModel.OnUseWrapper`, for any potion, a mod's included.
  - Regen and any other buff that heals at a turn boundary names whoever applied it, through the running effect the mod already tracks.
  - Healing yourself is never support, as with every other kind.
- **Known limit:** a heal from your own relic that fires in the middle of a teammate's card has no source the mod can see. It could be credited to the teammate whose card was playing. Vanilla has no such relic heal on a teammate's action; it's logged like every other gift, so a wrong credit would show up in `events.log`.

## On screen

- **Support tab:** a new card, **Healing**, with the game's heal intent icon.
- **A new award, Medic** ("HP restored to teammates"), with Regen's icon. Its minimum is 10.
- Shown on the exported image and the copied picture with the other kinds of help.
- Log: `Ash gave 12 healing to Moth | BLOOD_POTION`, read by the replay.

## Tests

- **Core:** `RecordSupport` with the new kind; a `healing` support line replays; Medic goes to the most healing given, at or above the minimum.
- **Core:** the healed amount is capped by missing HP (a pure helper).
- **Localization:** new keys in both files.
- **In a game:** Blood Potion thrown at a damaged teammate, in combat and on the map; Regen Potion on a teammate.
