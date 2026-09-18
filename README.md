# NixToUny

Migrador de Nixfarma a Unycop Next.

## Estado actual - Hito 2

La aplicación dispone de dos bloques funcionales:

### 1. Detección y conexión de Nixfarma

1. Busca `tnsnames.ora` usando `TNS_ADMIN`.
2. Busca `ORACLE_HOME\network\admin`.
3. Revisa los Oracle Homes registrados en Windows (32 y 64 bits).
4. Revisa rutas Oracle habituales.
5. Parsea los aliases TNS.
6. Ignora `EXTPROC*` y `HS_*`.
7. Prioriza el alias `NIXFARMA`.
8. Extrae `HOST`, `PORT` y `SERVICE_NAME` (o `SID`).
9. Construye la conexión Oracle con el usuario de consulta.
10. Verifica la conexión con `SELECT 1 FROM DUAL`.

### 2. Explorador Oracle de solo lectura

Después de validar la conexión se habilita **Explorar BD (solo lectura)**.

El explorador:

- descubre tablas y vistas accesibles;
- muestra columnas, tipo Oracle, nulabilidad y claves primarias;
- descubre relaciones de clave foránea entrantes y salientes;
- permite buscar por nombre de tabla o columna;
- propone candidatos heurísticos para:
  - artículos;
  - familias;
  - clientes;
  - créditos/deudas;
  - stock/existencias.

Las búsquedas de candidatos son orientativas. Ninguna tabla se considera correcta hasta revisar su estructura y relaciones.

## Garantía de solo lectura del explorador

`NixfarmaSchemaExplorer` no acepta SQL escrito por el usuario.

Las únicas consultas que ejecuta son `SELECT` fijos contra el diccionario Oracle:

- `ALL_TABLES`
- `ALL_VIEWS`
- `ALL_TAB_COLUMNS`
- `ALL_CONSTRAINTS`
- `ALL_CONS_COLUMNS`

El hito 2 no contiene `INSERT`, `UPDATE`, `DELETE`, `MERGE` ni DDL contra Nixfarma.

## Ejecutar

```powershell
.\setup.ps1
```

Después abre `NixToUny.sln` y ejecuta `NixToUny.App`.

## Siguiente paso

Usar el explorador sobre una instalación real de Nixfarma para identificar las tablas correctas de artículos, familias, clientes, créditos y stock. Con esa información se crearán repositorios tipados y consultas de extracción específicas.
