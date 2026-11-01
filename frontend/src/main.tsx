import React from "react";
import ReactDOM from "react-dom/client";
import App from "./App";
import { startSky } from "./sky";
import "./styles.css";

startSky(document.getElementById("sky") as HTMLCanvasElement);

ReactDOM.createRoot(document.getElementById("root")!).render(
  <React.StrictMode>
    <App />
  </React.StrictMode>,
);
