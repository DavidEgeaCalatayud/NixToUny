# NixToUny

Migrador de Nixfarma a Unycop Next.

## Estado actual - Hito 1

La primera version implementa unicamente la capa segura de descubrimiento de Nixfarma:

1. Busca `tnsnames.ora` usando `TNS_ADMIN`.
2. Busca `ORACLE_HOME\network\admin`.
3. Revisa los Oracle Homes registrados en Windows (32 y 64 bits).
4. Revisa rutas Oracle habituales.
5. Parsea los aliases TNS.
6. Ignora `EXTPROC*` y `HS_*`.
7. Prioriza el alias `NIXFARMA`.
8. Extrae `HOST`, `PORT` y `SERVICE_NAME` (o `SID`).
9. Construye la conexion Oracle con el usuario de consulta.
10. Verifica la conexion con `SELECT 1 FROM DUAL`.

No se ejecutan todavia consultas funcionales de migracion ni se escribe en Unycop Next.

## Ejecutar

```powershell
.\setup.ps1
```

Despues abre `NixToUny.sln` y ejecuta `NixToUny.App`.
