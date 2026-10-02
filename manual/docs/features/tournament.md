# Tournaments

RBM turns the arena into a ladder. In the base game every tournament draws in whichever lords and heroes
happen to be in town, whether you are a fresh level-1 adventurer or a famous champion. With
RBM, each tournament is pitched at **your tournament tier**: below tier 5 you fight regular troops of your
own tier, and only from tier 5 up do you enter the "main" tournament against the lords and heroes in town.
The arena gear, the prize and the renown you earn all scale with that tier.

The whole feature is controlled by the **RBM Tournament** toggle (on by default). Turning it off restores
the base game's tournaments. See [Settings](../settings.md).

## Your tournament tier

Your tier is worked out every time it is needed (when the tournament is set up, when the prize is chosen,
when you win), from two things, and the **higher** of the two counts:

| Source | How it is rated |
|---|---|
| Your level | Level 1-10 counts as tier 1, 11-15 as tier 2, 16-20 as tier 3, 21-25 as tier 4, 26-30 as tier 5, 31 and above as tier 6. |
| Your armor | The average tier of the armor pieces you wear in your battle equipment (head, body, arms, legs, cape), rounded. |

The result is always between 1 and 6. Because armor counts, a low-level character in good armor is rated
by the armor; a high-level character in rags is still rated by level.

!!! tip
    If you want an easier bracket, take your best armor off before entering the arena. If you want to reach
    the main tournament early, wear higher-tier armor.

When the tournament starts, a message tells you which bracket you are in: **Main tournament**, or
**Lower tier tournament: Tier N**.

## Who you fight

**Tiers 1 to 4 (lower tier tournament).** No lords or heroes take part. The field is filled with regular
troops of exactly your tier, drawn from the soldier troops of the town's culture plus the mercenary troops.
If no troop of your tier is available, the next lower tier is used.

**Tiers 5 and 6 (main tournament).** The town's notable fighters are invited first, in this order: leaders
of parties staying in the town, lords in the town without a party, other heroes in the town without a party
(such as wanderers), and heroes of your own clan in parties in the town. A hero is only invited if they are
rated tier 5 or higher by the same level-and-armor rule as you, and if they are an adult lord or wanderer who
is neither wounded nor a noncombatant. Any free places are then filled with tier 5 and 6 troops.

## Arena equipment

Before each match, RBM swaps the arena weapons so they match your tier, using items of the town's culture:

- **Shields, one-handed weapons and two-handed weapons** are replaced with culture items of your tier
  (a two-handed weapon may be swapped for a polearm that can swing). From tier 4 upward, the one-handed
  weapons are axes and maces only.
- **Bows, arrows, crossbows and bolts** are replaced with a fixed set that gets stronger with each tier
  (bows and arrows for tiers 1 to 6, crossbows and bolts for tiers 1 to 4).
- **Duels without shields.** In matches with one or two fighters per team there is a 50% chance that
  everyone in the match fights without a shield.

If the town's culture has no fitting item for a slot, the base game's arena item is kept.

## Prizes

The prize is a random item of the **town's culture**: a bow, crossbow, shield, weapon, mount or piece of
armor. It is chosen one tier above your tournament tier (tier 5 or 6 items when you are tier 5, tier 6 items
when you are tier 6). If the culture has no such item, the base game picks the prize. A prize is kept only
while it still matches your tier: when the game refreshes a town's prize and your tier has changed, a new
one is drawn.

When you win, the prize also rolls for a **quality modifier**. Each better-than-standard quality the item
can have (the ones that do not lower its price) gets a roll in turn; the first successful roll is applied.
A message reports each roll: what you rolled and what you needed.

If a non-player hero wins, their clan leader receives the prize's value in gold, as in the base game.

## Renown

The renown for winning grows with your tier:

| Tier | 1 | 2 | 3 | 4 | 5 | 6 |
|---|---|---|---|---|---|---|
| Renown | 3 | 5 | 6 | 8 | 9 | 11 |

The **Duelist** perk (One Handed) doubles the tier amount, and **Self Promoter** (Charm) adds its usual flat bonus on top.
Other heroes who win use the base game's renown reward.

## Simulated matches

When a match is simulated (for example after you are knocked out), it is resolved with the base game's
method, which pits each fighter's attack and defence values (with their arena gear) against the others.
RBM keeps that method and only makes sure every fighter has at least a minimal attack and defence value.
