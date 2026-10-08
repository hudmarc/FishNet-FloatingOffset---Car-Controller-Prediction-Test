# FishNet+Floating Offset Client Side Predicted Car Controller + Floating Offset Demo

`Built for Floating Offset 0.3`

This is my attempt at trying to adapt [an existing CSP car controller](https://github.com/Roceh/FishNet---Car-Controller-Prediction-Test) to use my [floating offset](https://github.com/hudmarc/FloatingOffset) package.



https://github.com/user-attachments/assets/794c6731-bb2b-4073-9e0f-b35f3f6c8e3e



## Known issues
- No camera smoothing
- ~~Desync on clients on scene transfer/rebase~~ Knowkn issue with PhysX Wheels. To fix use OffsetWheels on the root of your vehicles.
- Cinemachine does not currently work with FloatingOffset

## How to install
- Install FishNet from the Unity Asset Store
- Click "Add package from git URL..." in the Unity Package Manager (UPM) and paste in https://github.com/hudmarc/FFO-FishNet-Floating-Origin.git
- [ParrelSync](https://www.google.com/url?sa=t&source=web&rct=j&opi=89978449&url=https://github.com/VeriorPies/ParrelSync&ved=2ahUKEwiw7IHfqb-UAxXCUMMIHfykCa4QFnoECA8QAQ&usg=AOvVaw0eEHgZuqEmuzfgLX-tsBtY) is very helpful for locally testing multiple clients.

## FAQ

### Where is the main scene?

`CarController/Scenes/` contains the main game scene. The FloatingOffsetManager is built for a separated game scene and offline scene with the managers, but apparently this works too. Remember to add it to build settings before testing.

### Why is everything rebasing so often?
I set the minimum join distance quite low in the demo, which is quite low, so that the Floating Offset behavior is more obvious. This makes debugging easier because rebases/scene transfers etc happen much more often. If you want to change this, the settings are in the component `FishNetOfffsetManager` under `Configuration`

### Why do things pop in suddenly?

See the `SceneCondition` on the `ObserverManager`. It ensures that clients can only see other clients if they are in the same Offset Scene.

### How do I configure stuff?

To change offset settings etc look at `DefaultOffsetUniverse` and change the settings there.

To add a new tracked entity simply add an `OffsetView`. If it is a player, remember to set `IsPlayer = true`

---
#### Free assets used
Fishnet: https://assetstore.unity.com/packages/tools/network/fish-net-networking-evolved-207815

Floating Offset for Unity: https://github.com/hudmarc/FloatingOffset

Simple Car Controller: https://github.com/enisbt/SimpleCarController

3D Low Poly Car For Games: https://assetstore.unity.com/packages/3d/vehicles/land/3d-low-poly-car-for-games-tocus-101652

ENGINES: https://assetstore.unity.com/packages/audio/sound-fx/engines-123836
