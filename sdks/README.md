# DeleGuard beta clients / Clientes beta

Versions: .NET/Python/TypeScript 1.0.0 beta 1. Sources are local; packages have not been published to registries.

ES: Guarda `operation.id` con el trabajo. Calcula `operation.hash` sobre los argumentos exactos y canónicos en tu integración confiable. Construye los atributos de negocio leyendo tu sistema, no copiando afirmaciones del modelo. Entrega al cliente una función `currentSnapshot` que vuelva a leer esos hechos antes del consumo. La función de ejecución usa exactamente los argumentos preparados. No instales reintentos automáticos para POST ni para la función con efectos. Un error incierto se investiga en la bandeja de incidencias.

EN: Persist operation.id with the job. Hash the exact canonical arguments in the trusted integration. Read business facts from your system, rather than accepting model assertions. Supply currentSnapshot to re-read those facts before consuming. Execute exactly the prepared arguments. Never add automatic retries to POSTs or the side-effect callback. Investigate uncertain outcomes in the incident queue.

Python: install locally with `python -m pip install ./sdks/python`; import `Client` from `deleguard_client`. TypeScript: import Client from `sdks/typescript/client.ts` with a TypeScript build tool or supported Node type stripping. .NET: reference `sdks/dotnet/Deleguard.Client.csproj`, configure a dedicated HttpClient BaseAddress ending in `/`, bearer dgk_ credential and a finite timeout.

All clients implement authorize → poll → refresh snapshot → consume → execute → report. Approval polling does not itself execute tools. Never embed credentials in source or browser bundles. See [the contract](../deleguard_dev_pack/spec/execution-v1.md).
