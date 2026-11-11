# Ubuntu Docker and Portainer Migration Plan

## Goal and Decisions

- Move all listed container workloads and Shannon scan workers from Windows/WSL Containers to Docker on the Ubuntu Server at `192.168.4.47`.
- Windows remains an SSH/admin client. Keep local Unity-based Sonar scans on Windows, configured to use the remote SonarQube server.
- Use the dedicated mount `/dmount` for persistent container data. Do not leave persistent databases or caches in a user's home directory.
- Assume the user manually clones each Shannon target repository onto Ubuntu and passes its Linux path to the scan command. Worker containers and bind mounts run on Ubuntu; no Windows path translation or Windows container runtime remains.
- Restrict inbound access to the private LAN/VPN; expose only required service ports and restrict administrative/data ports by firewall. Do not publish database ports publicly.

## Components and Changes

1. **Ubuntu host bootstrap and storage**
   - Install supported Ubuntu Server updates, OpenSSH, Docker Engine from Docker's official Ubuntu repository, and the Docker Compose plugin.
   - Configure the Docker service to start on boot. Add the designated admin account to the `docker` group only if passwordless root-equivalent Docker access is acceptable; otherwise use `sudo docker`.
   - Verify `/dmount` is the intended mounted filesystem, mounted persistently through `/etc/fstab`, with adequate capacity. Create documented per-service directories under `/dmount` and set least-privilege ownership/modes before deployment.
   - Configure UFW and any upstream router/cloud firewall for private-client access only. Allow SSH from trusted admin addresses and only the chosen service ports from trusted clients.
   - Install Portainer CE as a Docker-managed service with persistent state under `/dmount`; expose its HTTPS UI on `9443` to trusted clients only. Do not expose Portainer Agent port `8000` unless an agent-based topology is explicitly chosen.
   - Reboot and verify Docker, `/dmount`, firewall, and Portainer all return as expected.

2. **Linux deployments and persistent data**
   - Create Portainer stacks or Ubuntu Compose deployments for Zengram/PostgreSQL, FalkorDB, SonarQube/PostgreSQL, GitHub Actions Unity runner, and Shannon Temporal. Replace WSL-specific Compose invocations with native `docker compose` behavior.
   - Review each Compose file's bind addresses, credentials, health checks, restart policy, and Linux file permissions. Use strong generated secrets; remove example/default passwords. Keep service-to-service database traffic on private Docker networks.
   - Relocate bind-mounted and named persistent data to `/dmount` (or Docker's data-root if intentionally configured there). Record a per-stack backup/restore mapping. Ensure backups are external to `/dmount` or copied off-host.
   - Zengram: persist PostgreSQL under `/dmount`; configure API reachability on the LAN and update MCP/API clients to `http://192.168.4.47:8084`. Keep PostgreSQL host port unpublished.
   - FalkorDB: copy the existing `dump.rdb` into its `/dmount` data directory and verify it loads before retiring the old copy. Do not expose `6379` beyond trusted clients; prefer Docker-internal access where possible.
   - SonarQube: persist data/extensions/logs and PostgreSQL under `/dmount`; publish `9000` only to trusted clients. Validate host kernel requirements and existing mmap option against the deployed SonarQube version. Set Windows `SONAR_HOST_URL` to `http://192.168.4.47:9000` and retain the Sonar token only in the local ignored `.env`.
   - GitHub runner: deploy as a persistent outbound-only runner stack with `/dmount`-backed work and Unity caches. Configure repository, runner labels, and a least-privilege registration credential in Portainer secrets/environment. Build and validate the Linux runner image; do not assume a Windows image is portable.
   - Shannon Temporal: persist its database under `/dmount`, expose gRPC `7233` only to the scan/admin clients that need it, and expose Web UI `8233` only to trusted clients. Verify the actual Temporal endpoint and namespace used by workers.

3. **Shannon workflow and settings migration**
   - Replace the Windows `shannon-wslc.ps1`, `shannon-common.ps1`, `shannon-infra.ps1`, `shannon-scan.ps1`, and `shannon-tools.ps1` container/worker orchestration with Ubuntu Bash scripts using `docker compose`/`docker`, Linux repo paths, and the remote Temporal endpoint. Include setup, up/down, start/stop, status, doctor/health, scan, and logs operations matching the current workflows.
   - Keep scanner, dynamic worker creation, browser tooling, and repository bind mounts on Ubuntu. Require a manually cloned repo path on the VM; validate the path, required `.shannon` directories, permissions, and model/config inputs before launching workers. Preserve read-only source mounts and mount deliverables/scratchpad separately as currently intended.
   - Replace WSL-only DNS and Chromium workarounds only after testing on native Docker; configure the VM's DNS/network explicitly where needed. Keep worker security settings least-privilege while validating Chromium requirements.
   - Document a one-time settings transfer from Windows: identify the Shannon `.env`, generated `models.json`, and any `.shannon` configuration/overlays in use; copy them over SSH/SCP into a restricted Ubuntu configuration directory, set owner/mode, and import secrets through Portainer/host environment files excluded from Git. Regenerate derived `models.json` on Ubuntu when possible rather than carrying stale generated values. Never include API keys in the plan, scripts, or commits.
   - Update `SHANNON_LLM_BASE_URL` and related model endpoint settings only if the Ubuntu VM cannot route to the current LLM gateway. Confirm the endpoint is reachable from inside a worker container.

4. **Other startup/manage scripts and applications**
   - `F:\Unity_projects\AttaxUnity\ci`: replace WSL container discovery/build/run logic in `_common.ps1`, `start_runner.ps1`, and `build_and_publish_image.ps1` with Ubuntu deployment/build instructions and Portainer stack management. Keep GitHub repository/runner configuration but remove Windows `wslc` dependencies.
   - `E:\ditrib\AI\Zengram\zengram.ps1` and `E:\ditrib\AI\falkovDB\falkordb.ps1`: replace local WSL-specific control paths with Linux Bash management scripts (up/down/status/logs/health/update/backup) or documented Portainer operations. Management must target the Ubuntu host and must not silently fall back to local containers.
   - `F:\github\repo\sonar-local`: retain `scan.ps1` as a Windows local scanner, but update its default server URL/configuration and health checks for `192.168.4.47`; move server startup/stop duties to the Ubuntu stack.
   - `F:\github\repo\merge-conflict-server\run-local-server.ps1`: add a Linux .NET 9 container image and Compose/Portainer deployment. Replace Windows temp/source paths with image content or explicit Linux bind mounts for `game-rules.json` and any persistent database. Configure `AllowedHosts`, API bind address, database provider/secrets, and port `5000`; validate HTTPS/reverse-proxy expectations before external exposure.
   - `E:\ditrib\AI\clean_and_compact_storage.ps1`: retire Windows WSL VHDX compaction. Provide a safe Ubuntu Docker cleanup command/script only if needed; default cleanup must not prune volumes or persistent `/dmount` data. Disk cleanup should be observable and require explicit confirmation for destructive actions.

## User-Performed Fresh-Server Steps

1. Install Ubuntu Server on the VM, apply updates, set hostname/time zone, create an admin account, and configure SSH keys. Record the static/reserved address `192.168.4.47` and confirm it is reachable from intended clients.
2. Attach and mount the dedicated data disk at `/dmount`; configure its UUID in `/etc/fstab`, verify it survives reboot, and check filesystem/capacity.
3. Install Docker Engine and Compose plugin from Docker's official Ubuntu repository; verify `docker version`, `docker compose version`, and daemon startup after reboot.
4. Configure UFW/upstream firewall for SSH from trusted admin devices and private-only access to required services. Default-deny unrelated inbound traffic. Keep Postgres ports private; avoid direct public exposure of FalkorDB and Temporal gRPC.
5. Install Portainer CE with its state on `/dmount`; save the initial admin credentials securely, enable HTTPS access, and restrict `9443` to trusted clients.
6. Create `/dmount` service directories and assign per-service ownership/permissions. Establish off-host backups before importing existing database state.
7. Copy approved Compose files and configuration to the server, set strong unique secrets, deploy stacks in dependency order, then verify health and restart persistence.
8. Copy Shannon settings securely, manually clone required repositories to Ubuntu, and verify a representative scan from end to end.
9. Update Windows client configuration for Zengram, FalkorDB if used directly, SonarQube, Temporal management if needed, and merge-conflict API. Remove/disable old WSL container startup paths after successful cutover.

## Migration and Rollout

1. Inventory existing data, credentials, image/build versions, ports, consumers, and rollback source for every service before stopping anything. Back up Zengram PostgreSQL, FalkorDB `dump.rdb`, SonarQube/PostgreSQL data, Shannon Temporal state, and runner cache as appropriate.
2. Prepare Ubuntu and validate `/dmount` and firewall before deploying services.
3. Deploy lower-risk/stateless services first, then databases and dependent applications. Restore data into the Linux volume layout and verify service-level health/API access.
4. Test Shannon with a manually cloned repository and confirm worker lifecycle, Temporal completion, deliverables, scratchpad, and logs. Test GitHub runner registration and a representative Unity build.
5. Update clients and scripts to `192.168.4.47`; retain old Windows data and scripts untouched until acceptance checks pass. Do not run both old and new writers against the same logical data.
6. After acceptance, stop old WSL stacks and deprecate their startup scripts. Retain verified backups through the agreed rollback period.

## Validation and Acceptance

- On Ubuntu, verify Docker/Compose and Portainer survive reboot, `/dmount` is mounted, each stack reports healthy, and all required data remains after container recreation.
- From a trusted client, test only intended endpoints: Zengram API `8084`, SonarQube `9000`, Temporal UI/gRPC as required, merge-conflict API `5000`, and Portainer HTTPS `9443`. Confirm database ports and unauthorized sources are blocked.
- Verify Zengram API/database operation, FalkorDB data restoration, SonarQube project access from Windows `scan.ps1`, runner registration/build execution, and merge-conflict API configuration/database behavior.
- Verify Shannon setup/status/health/scan/logs using the new Bash scripts and Linux repo paths, including failure behavior for missing repo/config and unreachable Temporal/LLM endpoints.
- Search all in-scope startup/manage scripts for `wslc`, `wslc-compose`, Windows drive paths, `Get-NetRoute`, and Windows process/AppX/VHDX dependencies. Any remaining occurrence must be explicitly limited to retired Windows-only tooling or Windows Sonar scanning.
- Run relevant repository tests/builds for the merge-conflict API and runner image; perform service smoke tests on the live VM. Record actual results and unresolved gaps in the implementation outcome.

## Risks / Open Operational Details

- Confirm the Ubuntu release/architecture and available disk capacity before choosing image tags and runner architecture.
- Confirm whether `192.168.4.47` is reserved/static and which client IP ranges should be allowed by UFW.
- Confirm whether services require TLS beyond Portainer; the current plan assumes private LAN/VPN access and does not introduce a public reverse proxy.
- The exact `/dmount` directory ownership layout, backup destination/retention, and migration downtime window must be set before data cutover.
- Shannon LLM gateway reachability and the precise set of local settings/secrets must be verified from the current `.env` and generated files during implementation.
- The merge-conflict service's production database/config behavior must be checked against its existing `run-local-server.json`, appsettings, and API expectations before container exposure.
