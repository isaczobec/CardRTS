Can you please create a system for buying cards? There should be a new input type for buying a card with field with the reference to the card, and a system with reads these inputs, deducts the gold if the client has enough, and adds the card to the clients deck. Please only spawn the card into the deck on the server, while the client can deduct the resources. Please create a new ResourcesDeducted request for this, and fire FlagEvents wherever appropriate.

Now i get these errors whenever a troop blinks (playing on the host): InvalidOperationException: Collection was modified; enumeration operation may not execute.
System.Collections.Generic.List`1+Enumerator[T].MoveNextRare () (at <f713e1c3c5f545ce9b1df47230f4f853>:0)
System.Collections.Generic.List`1+Enumerator[T].MoveNext () (at <f713e1c3c5f545ce9b1df47230f4f853>:0)
EventDispatcher`1[TBase].Flush () (at Assets/Scripts/Util/EventDispatcher.cs:42)
FlagEventManager.Flush () (at Assets/Scripts/ECS/FlagEventManager.cs:16)
TickManager.DoPredictionTick (System.Boolean isServer, System.Boolean isClient) (at Assets/Scripts/ECS/TickManager.cs:355)
TickManager.Update () (at Assets/Scripts/ECS/TickManager.cs:304)

InvalidOperationException: Collection was modified; enumeration operation may not execute.
System.Collections.Generic.List`1+Enumerator[T].MoveNextRare () (at <f713e1c3c5f545ce9b1df47230f4f853>:0)
System.Collections.Generic.List`1+Enumerator[T].MoveNext () (at <f713e1c3c5f545ce9b1df47230f4f853>:0)
EventDispatcher`1[TBase].Flush () (at Assets/Scripts/Util/EventDispatcher.cs:42)
FlagEventManager.Flush () (at Assets/Scripts/ECS/FlagEventManager.cs:16)
TickManager.TryRunServerTick () (at Assets/Scripts/ECS/TickManager.cs:387)
TickManager.Update () (at Assets/Scripts/ECS/TickManager.cs:314)

can you try to find the issue and fix it?

Can you to the multipoint card add a field for maximum range from the previous point, and clamp this both visually and when before the input (so that if i put my cursor further away than this range, the visual indicator is clamped to the max range, and that clamped position is also the position that will be inputtted)? The max range between points requirement should also be checked on the server before applying the inputs. Also add a max range between points for the blink card.

Can you make it so that the troops position relative to the center of the blink is kept on the destination? This should be done on the BlinkSystem. Also, on the Teleporting modifier system, can you add a check if the destination is inside a not traversable tile, and in that case find the closest walkable tile in one of the cardinal directions and make this the new destination tile?

Also, can you add a teleported flag to the movable component and make this true for one tick when the teleport modifier fires, and use this to make sure that the position interpolation is working correctly? Sometimes when blinking the troops start jittering back and forth.

Now, i want to create a source of gems. these i want to generate in clusters that are spawned in the following way: imagine drawing a line from every base to the center of the map. This entire shape should then be rotated 360 / (2n) degrees (where n is the amount of players/bases), and then one cluster should be placed at the end of each line (at some offset from the center). Half of the clusters (rounded down) should contain 3 objects and the other half should contain 7. Each object should drop 10 gems.

can you on the minimap manager add support for respawnable objects? Objects that are currently respawning should be rendered at 40% opacity.

Sometimes, when an enemy troop from another client chases a troop on this client, the position interpolation stops working correctly (it moves forward once per tick) and the movement animation stops - it just goes into its idle animation. The attacking animation plays correctly but afterwards the idle animation begins again when the troop starts moving towards my troop. Can you try to diagnose and fix this?