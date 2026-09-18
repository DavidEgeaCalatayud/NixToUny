# NixToUny

Migrador de Nixfarma a Unycop Next.

## Estado actual - Hito 3

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

### 3. Mapa de esquema y consultas de muestra

El explorador incorpora dos acciones nuevas:

- **Exportar mapa (.json)**: genera un informe con los mejores candidatos de cada área, columnas, PK/FK, score y una consulta de muestra sugerida. El JSON no contiene registros funcionales ni credenciales.
- **Copiar SELECT 20**: genera una consulta de solo lectura para la tabla o vista seleccionada, limitada a 20 filas mediante `ROWNUM <= 20`.

Flujo recomendado:

1. Exportar el mapa de esquema.
2. Analizar el JSON para identificar las tablas reales.
3. Ejecutar `SELECT 20` únicamente sobre las candidatas relevantes.
4. Usar esas muestras para definir el mapeo Nixfarma → Unycop Next.

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

Ejecutar el explorador en una instalación real, exportar el mapa JSON y analizarlo. Después se tomarán muestras de 20 filas solo de las tablas candidatas confirmadas para construir los repositorios tipados y el mapeo hacia Unycop Next.
