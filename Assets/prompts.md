# Map changes

Make the side length of the world half of the current size.

Make there be two intermittent islands on every bridge to the mid island instead of one.

Remove the side bridges between the islands of the players.

## Changes to the conquerable entities ("Spawn crystals") on the intermittent islands

Please make it so that the spawn crystals begin as friendly to the player whose base they are closest to. The one closest to mid should have 1000 HP, and the one closest to the base 1500 HP. Make the player bases have 2000 HP. 

When a spawn crystal dies, it should respawn with half its max health.

Players should only be able to attack the 'outermost' spawn crystal that a player owns on that players bridge; the inner one should have all damage set to 0 if there is a spawn crystal further from the base that the defending player owns. If the player owns any of the spawn crystals on their own bridge, all damage against their base should be reduced to 0. If a player has lost a spawn crystal on their own bridge, they should not be able to attack the spawn crystals of others before recapturing their own one. If they do not own any of their local spawn crystals, they should have to conquer the one closest to their base before retaking the one closest to mid. 

# Changes to deck building

Please change the hard limit of 9 cards in a deck to instead be a limit of max 4 troops, max 2 buildings, and max 2 spells. Make the first 8 cards the player buys cost 10 gold instead of the first 6 cards. 

Please also make double right-clicking a card refund that card instead of discarding it. It should be removed from the players hand and deck, and the player should be refunded 50% of the FULL PRICE (not of 10 gold for the first 8 cards, but of those cards full price), and 50% of the gold worth of the upgrades attached to those cards. The upgrade entities should of course also be removed when a card is sold. Please also make refunding play a sound using the audiomanager.

# Changes to resources

I want to remove metal and soulstones as resources from the game, and only keep wood, stone, gems, and gold. You should not remove soulstones and metal from the codebase entirely as this is still an experimental build. Instead, i want you to look over the wood/stone/metal resource costs of all cards in the game, and remove metal and soulstones. For the troops, i want the mental model to be that lighter, faster troops usually cost mostly wood while heavier troops cost more stone (but many troops should still consume both resources). The sum cost of basic resources for the troops should still be the same. Buildings should follow roughly the same logic. concerning soulstones, simply make the troops that cost these more expensive in the other resources. For spells, make all of these only cost gems, and try to make the gem cost proportional to the strength/current cost of the spells. 

I also want to introduce a soft cap on the amount of wood/stone at 500 of each resource. Under this cap, each player should have a passive generation of 2.5 of these resources per second. Over this limit, all gain of wood and stone should be reduced by 50%. Furthermore, there should no longer be any passive generation of stone, wood, or gold gained from destroying resources in the world. However, please increase the amount of stone, wood, and gold dropped from trees and stones by 150% each. Please also increase the gold drop from the mid gold crates by 30% and the gold drop from killing enemy troops/buildings by 30%. Killing another players respawn crytal should from now on also drop 200 gold. for the soulstones, these entities should still spawn on the map, but they should instead drop 150 gold, 40 gems, and 250 wood and stone. 

Please also remove the resource clusters spawning on the bridges.