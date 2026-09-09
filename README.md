# MGS4 Master Collection Trainer

This trainer lets you change parts of *Metal Gear Solid 4* while the game is running. It connects to `mgs4.exe`, reads supported game values, and lets you edit them.

## Starting the Trainer

1. Start MGS4.
2. Load into gameplay.
3. Start the trainer as administrator.
4. Wait for the trainer to finish preparing itself.

Some controls may be disabled at first. They become available after the trainer finds the player, inventory, and other required game data.

## Cheats

The **Cheats** menu contains:

- **Infinite Life**: Snake's health does not decrease.
- **Infinite Ammo**: Ammunition is not used.
- **Infinite Suppressor**: Suppressor durability does not decrease.
- **Never Reload**: The current magazine count does not decrease.
- **No Stress**: Prevents new stress from being added. It does not remove stress already gained.
- **Infinite Camo**: Forces camouflage effectiveness to 100%. The game may still display 99%.

These cheats can be turned on and off while playing.

## Filters

The **Filters** menu changes visual or performance settings:

- **Disable Motion Blur**
- **Disable Screen Filter**
- **Disable Resolution Scaling**

Some changes only become visible after moving to another area.

## Inventory

The **Recovery Items** menu lets you edit:

- Rations
- Noodles
- Regain
- Pentazemin
- Compress
- Cigarettes
- Muna
- Syringes

For most recovery items, you can set both the current amount and the maximum amount.

Enter `-1` to remove an item from the inventory.

The **Gadgets** menu lets you add or remove:

- Bandana
- Stealth camouflage
- Camera
- Radio
- Scanning plug
- Drum can

You can also edit C. Box durability:

- `-1` removes the C. Box.
- `0` to `25` sets its durability.

After an inventory change, the trainer reads the value again to check that it was applied correctly.

## Player Values

The main form can display and edit:

- HP
- Max HP
- Psyche
- Max Psyche
- Battery
- Max Battery
- Drebin Points

These values update about once per second.

To change one:

1. Type a new value.
2. Press Enter or leave the field.
3. The trainer writes the value to the game and verifies it.

The arrow controls change regular values by `100` and Drebin Points by `5,000`.

## Mission Stats

The **Stats** menu includes:

- Difficulty
- Play time
- Alerts
- Continues
- Recovery items used
- Headshots
- Knife kills
- Knockouts
- CQC uses
- Combat highs
- Hold-ups
- Body searches
- Praises
- Items donated
- Weapon and item pickups
- Syringe and scanning plug uses
- Magazine pages
- Crouch, crawl, and wall-press time
- Box and drum time
- Rolls
- Flashbacks

### Editing Stats

1. Change the values you want.
2. Choose a difficulty if needed.
3. Press **Change All**.

The trainer validates every value before writing anything.

Time values must use this format:

```text
HH:MM:SS
```

For example:

```text
01:23:45
```

Use **Refresh Stats** to discard your edits and reload the values from the game.

### Live Tracking

**Disable Editing and Track Stats Live** switches the page to read-only mode.

In this mode:

- Statistics update every second.
- The fields cannot be edited.
- Any unfinished edits are replaced with the current game values.

Press the button again to return to editing mode.

## Projected Rank

The trainer calculates a projected rank using information from the current run, including:

- Time
- Alerts
- Continues
- Recovery item usage
- Other mission statistics

This is an estimate. The final rank shown by the game may be different.

## Stage Loading

The stage controls allow you to:

1. Refresh the available stage list.
2. Select a stage.
3. Press **Change**.

The trainer uses the game's stage-loading code to queue the transition.

> **Warning:** Changing stages may reset Snake's inventory.

## Debugger Tool

The **Open Debugger Tool** button opens a separate tool for advanced inspection.

It can be used to inspect:

- Game values
- Memory
- Effects
- Freezes
- Signatures
- Stage information
- Rank information
- Unlock actions

Most normal trainer features are available from the main form. The debugger is mainly for testing, troubleshooting, and advanced use.

## Closing the Trainer

When the trainer closes, it attempts to restore the hooks and memory changes that it owns.

If cleanup fails, the problem is recorded in the trainer log beside the executable.

## Summary

The main form lets you:

- Enable gameplay cheats.
- Edit Snake's inventory.
- Add or remove gadgets.
- Change health, Psyche, battery, and Drebin Points.
- Edit mission statistics.
- Track statistics live.
- Preview your rank.
- Remove visual effects.
- Load another stage.
- Open advanced debugging tools.
