# Polaris

Gestión inteligente de espacios (Logística + Estacionamiento) con iluminación conectada. Demo para la FAPPE.

## Carpetas

| Carpeta | Qué es | Dirección |
| --- | --- | --- |
| `backend/` | API en ASP.NET Core (.NET 10) + PostgreSQL en Docker. La lógica, la base y los ESP32. | http://localhost:5080 (docs: /scalar) |
| `frontend/` | Página web en React + TypeScript. Lo que se muestra en la FAPPE. | http://localhost:5173 |

## Cómo arrancarlo (Visual Studio Code)

1. Abrí Docker Desktop y esperá a que diga **Engine running**.
2. Abrí la carpeta `polaris` en Visual Studio Code (Archivo → Abrir carpeta).
3. Apretá **F5**. Se levantan la base, el backend y el frontend, y se abre la página en el navegador.

Para arrancarlo sin abrir el navegador: **Ctrl+Shift+B**.
Para apagar todo: en el panel Terminal, tacho de basura en cada terminal (o cerrar VS Code).

Cada parte queda en su propia terminal abajo:
- **2. Backend - API (localhost:5080)**: cuando dice `Now listening on`, está listo.
- **3. Frontend - Web (localhost:5173)**: cuando dice `Local: http://localhost:5173`, está listo.

## Simular los ESP32 (sin hardware)

En una terminal nueva de VS Code:

```
cd backend
dotnet run --project src/Polaris.DeviceSimulator
```

## Tests

Terminal → Ejecutar tarea → **Correr tests del backend**.

## Puertos

- Base de datos: **55432** (5432 y 5433 los usan los PostgreSQL 17 y 18 instalados en Windows).
- Backend: 5080 · Frontend: 5173.
