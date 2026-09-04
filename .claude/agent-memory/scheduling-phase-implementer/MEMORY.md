# Memory Index

- [BackgroundService.StartAsync race](feedback_backgroundservice_startasync_race.md) — it's fire-and-forget via Task.Run; test harnesses must poll for a started signal, never assume sync prefix ran
- [Scheduling lock composition correction](project_scheduling_lock_composition_correction.md) — distributed-scheduler locks must be keyed per occurrence and never released early, or cross-replica duplicate firing happens
- [Shared-file protocol under concurrent dispatch](project_shared_file_protocol_concurrent_dispatch.md) — root state-map.md/CLAUDE.md/.slnx are off-limits when multiple domain agents run concurrently
