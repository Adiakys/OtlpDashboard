# Demo widget library

`demo-pack/` is a sample widget library shipped with the repo so you can
verify the loader without authoring widgets first. The Docker compose
stack bind-mounts it into the container at
`/app/data/widget-libraries/demo-pack` (read-only), so after
`docker compose up --build` the picker shows a **demo-pack** section
with six widgets:

| Widget               | Engine | Wraps           | Notes                                             |
|----------------------|--------|-----------------|---------------------------------------------------|
| Welcome              | preset | `text`          | Renders without a metric binding                  |
| Request rate         | preset | `metric-stat`   | `ops` unit, mean over last 15m                    |
| p99 latency (ms)     | preset | `metric-stat`   | Thresholds at 200/500 ms                          |
| Error rate gauge     | preset | `metric-gauge`  | Percentage gauge, thresholds at 1%/5%             |
| Recent traces feed   | preset | `recent-traces` | Errors-first sort, 25 rows                        |
| Demo Vega-Lite       | spec   | —               | Demonstrates the "engine not available" placeholder until iter 2 |

For local development outside Docker, copy or symlink `demo-pack/` into
`src/OpenTelemetryDashboard.Host/data/widget-libraries/`.
