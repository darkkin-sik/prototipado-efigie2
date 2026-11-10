-- Respuestas de la encuesta (mismas columnas que respuestas_app.csv).
CREATE TABLE respuestas (
    id TEXT PRIMARY KEY,
    nombre TEXT NOT NULL DEFAULT '',
    anio TEXT NOT NULL DEFAULT '',
    facultad TEXT NOT NULL DEFAULT '',
    etapa TEXT NOT NULL DEFAULT '',
    estacion TEXT NOT NULL DEFAULT '',
    grupal TEXT NOT NULL DEFAULT '',
    formato TEXT NOT NULL DEFAULT '',
    rol TEXT NOT NULL DEFAULT '',
    device_id TEXT NOT NULL DEFAULT '',
    creado TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);
CREATE INDEX respuestas_device ON respuestas (device_id);

-- Historial de configuraciones de la efigie; la vigente es la de mayor id.
CREATE TABLE config_versiones (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    config TEXT NOT NULL,
    nota TEXT NOT NULL DEFAULT '',
    creado TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);
