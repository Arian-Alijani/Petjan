# PostgreSQL 18 memory profiles

Every value in these files is a **starting point**, not a capacity or latency guarantee.
Apply through the vendor-operated PostgreSQL configuration and validate via G07 correctness/restore
and G08 constrained-host performance gates before production. The application never applies these files.

The 2 GiB profile allows 30 connections; the 4 GiB profile allows 40. Current default pools total 12
(6 write + 3 read + 1 key ring + 2 worker), leaving at least 8 slots for administration and backup.
When pool sizes change, keep `max_connections >= pool sum + 8`; readiness warns by probe name only.
`statement_timeout` is an application-role starting point; reporting uses SET LOCAL 5s and the worker pool.
Keep synchronous_commit/full_page_writes/autovacuum enabled for durability; do not trade them for benchmarks.
The host uses concurrent workstation GC, a 70% heap budget, ConserveMemory=5 and TieredPGO, without ReadyToRun/AOT.
