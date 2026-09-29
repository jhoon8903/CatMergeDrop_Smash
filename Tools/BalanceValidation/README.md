# Balance validation

> Historical baseline: these Python economy scripts model the pre-cumulative-drop rules (brick reset, success cat consumption). Do not use their results to certify the new four-Drop attempt loop. Use the Unity StageAttemptSaveTests, StageAttemptRewardTests and CumulativeDropTests for the new contract.

Read-only diagnostics for the current authored game. These tools never load or modify a player save or change production assets.

1. Run `export_oracle.cs.txt` through Unity's execute-code tool in the **CatMergeDrop_Smash** editor, in edit mode. It creates and disposes isolated ECS worlds and writes `Library/StageGeneration/balance-oracle.json`.
2. `python3 Tools/BalanceValidation/validate_model.py` compares 98 actual ECS cases with the Python battle model (outcome, ordinary gold, raw special-brick counts, frame count).
3. `python3 Tools/BalanceValidation/check_catalog.py` tests three equal-level cats in each lane of all 1,000 stages. Level is `stage.bit_length()`. This supplies cats artificially and therefore does not establish affordability.
4. `python3 Tools/BalanceValidation/run_economy.py` runs 20 seeds, three policies, and two initial-gold scenarios. Output: `Library/BalanceValidation/economy-report.json`.

Policies merge automatically and spread cats, merge automatically and focus up to three cats, or delay merges to preserve headcount and focus. Focus policies rotate lanes after failure; none reads the lane's bricks in advance. A preparation buys until its current board-space or gold budget is exhausted, then merges and drops. It does not repeatedly buy/merge before dropping. Reserve upgrades, sales, deliberate long-term ticket hoarding, and optimized planning are not modeled. Ticket rewards are opened immediately after success. Python RNG reproduces distributions, not Unity's seed sequence.

The actual scenario reads starting gold from PlayerSaveData. The second scenario starts with three first-summon costs (currently 60G), only in simulation, to diagnose progression past the starting bottleneck. This is not a recommended balance change. Historical merged maximum persists; success consumes submitted cats; failed runs preserve cats and ordinary gold but do not bank special tickets. Merge-mass and submitted-cat consumption invariants are checked during runs.

`policy_blocked` means this particular policy has no remaining modeled action, not that every possible player strategy fails. Caps (100 attempts per stage, 20,000 battles per seed, 480 frames per battle at dt=0.05) are censored results. Combat seconds exclude player decisions, preparation, animation overhead, and UI. Results do not measure human win rate, fun, or retention.

The model records hashes of its primary input assets and gameplay sources. Layout constants are copied from the current implementation; recalibrate the actual ECS oracle after gameplay changes. A passing finite oracle is evidence of agreement on those cases, not proof for all possible states. Library outputs can be deleted by Unity; the dated report in Docs retains the interpretation and summary.
