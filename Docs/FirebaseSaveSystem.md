# Save System

## Structure

`Assets/Framework/Data/` owns the reusable `SaveSystem`, `ISaveData`, `SaveDataDefinition`, and storage adapters. `Assets/Framework/BootStrap/SaveSystemBootstrapStep` is the ScriptableObject step that selects Local or Network, identifies the game-specific data definition, and loads it before scene initialization.

This project's `PlayerSaveData` and `PlayerSaveDefinition` live in `Assets/Projects/Scripts/Data/`. For another game, keep the framework code and provide a new `ISaveData` model and `SaveDataDefinition` asset. Assign that asset to the boot step.

Local mode writes `Application.persistentDataPath/saves/{saveKey}.json`, replacing the previous file only after the new JSON is written. Network mode uses anonymous Firebase Authentication and Cloud Firestore at `users/{uid}/saves/{saveKey}` by default. The first load writes the default save when no document exists. Local JSON and Firestore use the same project save model, but Firestore stores its fields directly. Switching modes does **not** migrate an existing local save to the server.

For this game, `users/{uid}/saves/player` contains `schemaVersion`, `savedAtUnixMilliseconds`, `progress`, `gold`, `board` (12 integer levels, index 0–11), and `inventory`. `PlayerSaveDefinition` maps these fields to `PlayerSaveData`; another game provides a different `IFirestoreSaveCodec`. An older Firestore document with a `payload` string can still be read and is converted to direct fields on its next write.

The `SaveSystemBootstrapStep` SO exposes Network settings: Firebase Project ID, Firestore Database ID (`(default)` for the default database), Users Collection, and Saves Collection. The Project ID is checked against the Firebase app loaded from the platform configuration, preventing a save from silently targeting the wrong project. Collection names determine the document path. These SO fields do not replace Firebase's Android/iOS configuration files.

## Development and release verification

Keep `Assets/Projects/Assets/Configuration/SaveSystemBootstrapStep.asset` in **Local** mode during ordinary development and gameplay testing. Before a service build, verify the actual Network path with a test Firebase project/account:

1. Confirm the platform's Firebase config file, app ID, Anonymous Auth provider, Firestore database, and rules are present. Enter the matching Project ID and database ID in the step SO.
2. Set the step asset to **Network**. Start with a fresh test account and confirm the first load creates valid default `PlayerSaveData` in memory.
3. Change a known value through `SaveSystem.UpdateAsync<PlayerSaveData>`; check the Firestore document under `users/{uid}/saves/player` contains the new `progress`, `gold`, and `board` fields.
4. Stop and restart the app with the same account. Confirm `GetSnapshot<PlayerSaveData>()` returns that value, then change it again and confirm a second save/load round trip.
5. Test network failure and malformed/unsupported save behavior. These currently fail boot; decide recovery UX before release.
6. Build only after the target platform passes the round trip. Keep a record of the tested build, platform, Firebase project, account UID, and result. Restore Local mode for continued prototype work.

The Android, iOS, and generated desktop Firebase config files are present. After publishing owner-scoped Firestore rules, a Play Mode first launch created `users/{uid}/saves/player` with the expected direct fields. A later Play Mode launch read the existing document (`exists=True`) and reached GameScene. One intervening launch stalled during bootstrap without an error, so repeated-start reliability and target-device behavior still need verification before a service build. The step asset was restored to Local mode after testing. The Firebase Unity SDK advises using a separately named app instance for Editor EditMode plugins, while the game's repository uses the default app instance for normal gameplay.

## Gameplay use

Resolve `SaveSystem` from the boot VContainer scope. Boot has already called `LoadAsync`, so game code can read a detached copy and update through one serialized operation:

```csharp
PlayerSaveData current = saveSystem.GetSnapshot<PlayerSaveData>();
await saveSystem.UpdateAsync<PlayerSaveData>(data => data.SetGold(data.Gold + 10), cancellationToken);
```

Use the candidate passed into `UpdateAsync` for decisions that must include the latest queued save, rather than relying on an older snapshot. Validation runs before persistence; the in-memory state changes only after the write succeeds. The first boot persists a new player's defaults; call `SaveCurrentAsync` to rewrite the current snapshot without changing it. Save on meaningful progression changes, not every frame.

Unknown schema versions and malformed data currently fail boot. Add explicit migrations and recovery UX before changing `CurrentSchemaVersion` or shipping to users.

## Firebase deployment checklist

Auth and Firestore packages are referenced in `Packages/manifest.json` and in the generated C# project. The platform Firebase configuration files are present under `Assets/`, but Network mode has not passed a live backend round trip yet.

1. Register the actual Android/iOS application IDs in Firebase and place `google-services.json` / `GoogleService-Info.plist` in `Assets/` as appropriate.
2. Enable Anonymous sign-in in Firebase Authentication.
3. Enter the Firebase console's Project ID and the Firestore database ID in the step SO, then deploy rules before switching its `storageMode` to Network. Installing Realtime Database does not create a Firestore database; this adapter uses Firestore.
4. Test sign-in, first load, update, restart, and second-device behavior with a test project. Account linking is needed if saves must survive reinstall or move between devices.

Example prototype rules (adjust and deploy for the real project):

```text
rules_version = '2';
service cloud.firestore {
  match /databases/{database}/documents {
    match /users/{uid}/saves/{saveKey} {
      allow read: if request.auth != null && request.auth.uid == uid;
      allow create, update: if request.auth != null
        && request.auth.uid == uid
        && request.resource.data.keys().hasOnly([
          'schemaVersion', 'savedAtUnixMilliseconds', 'progress',
          'gold', 'board', 'inventory'
        ])
        && request.resource.data.keys().hasAll([
          'schemaVersion', 'savedAtUnixMilliseconds', 'progress',
          'gold', 'board', 'inventory'
        ])
        && request.resource.data.schemaVersion == 1
        && request.resource.data.savedAtUnixMilliseconds is int
        && request.resource.data.progress is int
        && request.resource.data.progress >= 1
        && request.resource.data.gold is int
        && request.resource.data.gold >= 0
        && request.resource.data.board is list
        && request.resource.data.board.size() == 12
        && request.resource.data.inventory is list;
      allow delete: if false;
    }
  }
}
```

These rules isolate users and validate the top-level shape but do not make gold or rewards trustworthy: a modified client can write its own values. Server-authoritative economy changes need server-side validation (for example, Cloud Functions). The current Network adapter also uses last-write-wins across devices and has no offline synchronization policy.
