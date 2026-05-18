# FishNet+Floating Offset Client Side Predicted Car Controller + Floating Offset Demo

This is my attempt at trying to adapt an existing CSP car controller to use my floating offset package.

## Known issues
- ~~Jitter on clients~~ Mostly fixed, seemed to be caused by running the physics loop on PostTick instead of Tick
- No camera smoothing
- ~~Desync on clients on scene transfer/rebase~~ To fix this use OffsetRigidbody and OffstWheels on the root of your vehicles.
- Cinemachine does not work with FloatingOffset, I'm investigating what I could do to fix this

## How to install
- Install FishNet from the Unity Asset Store
- Click "Add package from git URL..." in the Unity Package Manager (UPM) and paste in https://github.com/hudmarc/FFO-FishNet-Floating-Origin.git
- [ParrelSync](https://www.google.com/url?sa=t&source=web&rct=j&opi=89978449&url=https://github.com/VeriorPies/ParrelSync&ved=2ahUKEwiw7IHfqb-UAxXCUMMIHfykCa4QFnoECA8QAQ&usg=AOvVaw0eEHgZuqEmuzfgLX-tsBtY) is very helpful for locally testing multiple clients.

## FAQ

### Where is the main scene?

`CarController/Scenes/` contains the main game scene. The FloatingOffsetManager is built for a separated game scene and offline scene with the managers, but apparently this works too. Remember to add it to build settings before testing.

### Why is everything rebasing so often?
I set the minimum join distance to 50 meters, which is ridiculously low, so that the Floating Offset behavior is more obvious. This makes debugging easier because rebases/scene transfers etc happen much more often.

### Why do things pop in suddenly?

See the `OffsetCondition` on the `ObserverManager`. It ensures that clients can only see other clients if they are in the same Offset Scene.

### How do I configure stuff?

To change offset settings etc look at `DefaultOffsetUniverse` and change the settings there. `OffsetCondition` lets you change when clients are shown objects.

To add a new tracked entity simply add an `OffsetTransform` and set `isView` to `True`




https://github.com/user-attachments/assets/92f08723-80dd-4d44-baaa-3fd25da893e1

https://github.com/user-attachments/assets/b98324eb-2825-4b8f-8f15-97acf8e4f958





---
#### Free assets used
Fishnet: https://assetstore.unity.com/packages/tools/network/fish-net-networking-evolved-207815

Floating Offset for Unity: https://github.com/hudmarc/FloatingOffset

Simple Car Controller: https://github.com/enisbt/SimpleCarController

3D Low Poly Car For Games: https://assetstore.unity.com/packages/3d/vehicles/land/3d-low-poly-car-for-games-tocus-101652

ENGINES: https://assetstore.unity.com/packages/audio/sound-fx/engines-123836
