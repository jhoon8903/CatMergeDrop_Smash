"""Compare the read-only battle model to isolated actual Unity ECS executions."""
import argparse
import json
from pathlib import Path
from battle_model import load_snapshot, simulate


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--root', type=Path, default=Path(__file__).resolve().parents[2])
    parser.add_argument('--oracle', type=Path)
    parser.add_argument('--output', type=Path)
    args = parser.parse_args()
    source = args.oracle or args.root / 'Library/StageGeneration/balance-oracle.json'
    output = args.output or args.root / 'Library/StageGeneration/balance-model-validation.json'
    snapshot = load_snapshot(args.root)
    cases = json.loads(source.read_text())
    failures = []
    max_step_difference = 0
    for case in cases:
        actual = simulate(snapshot, case['stage'], case['cats'])
        discrepancies = {key: {'ecs': case[key], 'model': actual[key]}
                         for key in ('won', 'phase', 'normal_gold', 'gold_tickets', 'item_tickets')
                         if case[key] != actual[key]}
        difference = abs(case['steps'] - actual['steps'])
        max_step_difference = max(max_step_difference, difference)
        if difference > 1:
            discrepancies['steps'] = {'ecs': case['steps'], 'model': actual['steps']}
        if discrepancies:
            failures.append({'stage': case['stage'], 'policy': case['policy'],
                             'cats': case['cats'], 'differences': discrepancies})
    result = {'cases': len(cases), 'passed': len(cases) - len(failures),
              'failed': len(failures), 'step_tolerance': 1,
              'max_step_difference': max_step_difference, 'failures': failures,
              'source_hashes': snapshot['source_hashes']}
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(result, indent=2, ensure_ascii=False))
    print(json.dumps({k: v for k, v in result.items() if k not in ('failures', 'source_hashes')}))
    for failure in failures[:5]:
        print(json.dumps(failure))
    raise SystemExit(bool(failures))


if __name__ == '__main__':
    main()
