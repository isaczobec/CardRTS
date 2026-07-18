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

Can you to the multipoint card add a field for maximum range from the previous point, and clamp this both visually and when before the input (so that if i put my cursor further away than this range, the visual indicator is clamped to the max range, and that clamped position is also the position that will be inputtted)? Also add a max range between points for the blink card.