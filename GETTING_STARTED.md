# Get Creative Longform running (simple setup)

This guide is for **anyone** who can install an app and copy text into a window—**no programming experience required**. You will use **Docker**, which packages the writing app, database, and AI helper so they run together on your computer.

**What you get:** a web-based creative writing tool in your browser, usually at [http://localhost:8080](http://localhost:8080).

**Rough time:** 20–45 minutes the first time (mostly downloading). Later starts are much faster.

---

## Before you start

Check these off mentally:

| You need | Why |
|----------|-----|
| A **Windows**, **Mac**, or **Linux** computer | The app runs locally on your machine |
| **Administrator** (or install) permission once | To install Docker |
| A **stable internet** connection | First-time downloads are large |
| **About 10 GB free disk space** | Safer with room for Docker images and one AI model |
| **Patience** for the first run | Downloads can take a while; that is normal |

You do **not** need to know what Git, .NET, or Angular mean.

---

## Part 1 — Install Docker

Docker is free. Install **Docker Desktop** (easiest on Windows and Mac):

1. Open: **[https://www.docker.com/products/docker-desktop/](https://www.docker.com/products/docker-desktop/)**
2. Download Docker Desktop for **your** system (Windows, Mac with Apple Silicon or Intel, or Linux).
3. Run the installer and **restart** if it asks you to.
4. Start **Docker Desktop** from your Start menu / Applications / launcher.
5. Wait until Docker says it is **running** (whale icon in the taskbar or menu bar is steady, not “starting”).

**Windows note:** Docker may ask to enable **WSL 2** or install components—allow that. If you get stuck, use Microsoft’s “Install WSL” guide linked from Docker’s Windows install page.

**Linux note:** If you prefer not to use Docker Desktop, install **Docker Engine** and the **Compose plugin** from Docker’s Linux docs, and ensure your user can run `docker` (often: add yourself to the `docker` group and log out and back in).

---

## Part 2 — Download this project

You need one file from this project: **`docker-compose.prod.yml`**. The simplest way is to download the **whole repository** as a ZIP (you will not edit code).

1. Open: **[https://github.com/jholovacs/CreativeLongform](https://github.com/jholovacs/CreativeLongform)**
2. Click the green **Code** button, then **Download ZIP**.
3. Unzip the file anywhere you like, for example your **Documents** folder.
4. You should see a folder named something like **`CreativeLongform-main`**. Remember where it is—you will open a terminal **inside** that folder in the next part.

---

## Part 3 — Open a terminal in the project folder

The steps depend on your system.

### Windows 10 / 11

1. Open **File Explorer** and go into the unzipped folder (`CreativeLongform-main` or similar) until you see **`docker-compose.prod.yml`** in the list.
2. Click the address bar at the top, type **`cmd`**, press **Enter**.  
   *Or:* **Shift + right‑click** in empty space → **Open in Terminal** / **Open PowerShell window here**.

### Mac

1. Open **Finder** and go into the unzipped folder until you see **`docker-compose.prod.yml`**.
2. **Right‑click** the folder in the title bar (or use **Finder → Services**) and open Terminal here, *or* open **Terminal** from Applications, type `cd ` (with a space), drag the folder into the window, press **Enter**.

### Linux

1. Open your terminal emulator.
2. `cd` into the unzipped folder (the one that contains **`docker-compose.prod.yml`**).

---

## Part 4 — Start the app (copy and paste)

Make sure **Docker Desktop** (or Docker Engine) is **running**.

In the same terminal window, **copy each block below**, paste it, press **Enter**, and wait until it finishes before the next block.

**1) Download the application images** (may take several minutes):

```text
docker compose -f docker-compose.prod.yml pull
```

**2) Start everything in the background:**

```text
docker compose -f docker-compose.prod.yml up -d
```

**3) Download a small AI model** used for writing help (one-time; can be 1–2 GB or more):

```text
docker compose -f docker-compose.prod.yml exec ollama ollama pull llama3.2
```

Wait until you see a success message. If something errors, see [Troubleshooting](#troubleshooting) below.

---

## Part 5 — Use the app

1. Open your web browser (Chrome, Edge, Firefox, or Safari).
2. Go to: **[http://localhost:8080](http://localhost:8080)**

You should see the Creative Longform interface. If the page does not load, wait a minute (the API may still be starting) and refresh.

---

## When you are done for the day

You can leave Docker running or stop the stack.

To **stop** the app but keep your data (stories, models on disk):

```text
docker compose -f docker-compose.prod.yml stop
```

To **start** it again later (after you rebooted, open **Docker Desktop** first, open a terminal in the project folder, then):

```text
docker compose -f docker-compose.prod.yml up -d
```

That re-starts the stack if it was stopped. (Use the same folder that contains `docker-compose.prod.yml`.)

---

## Troubleshooting

### “Docker” command not found / not recognized

- Install Docker Desktop and **restart** the computer.
- On Windows, open a **new** Command Prompt or PowerShell after installing.

### Docker says it cannot connect / daemon not running

- Open **Docker Desktop** and wait until it is fully started (not “Starting…”).

### Port 8080 already in use

Something else is using that port. You can set a different port in a file named **`.env`** next to `docker-compose.prod.yml`:

```env
WEB_PORT=8888
```

Then run:

```text
docker compose -f docker-compose.prod.yml up -d
```

Open **[http://localhost:8888](http://localhost:8888)** instead.

### `docker compose ... pull` fails with “denied” or “unauthorized”

The app images must be **public** on GitHub Container Registry. If you maintain the project, open each package on GitHub → **Package settings** → set visibility to **Public**. If you are an end user, use a release where the maintainer has published public images.

### `ollama pull` fails or is very slow

- Check your internet connection.
- Try again later; the model servers can be busy.
- Ensure you have enough disk space.

### Page loads but shows errors about the API

- Wait 1–2 minutes after `up -d` and refresh.
- In Docker Desktop → **Containers**, check that **creative-longform-api** is running (green).

### You are stuck

1. Confirm Docker Desktop is running.
2. Confirm you ran the commands **from the folder** that contains **`docker-compose.prod.yml`**.
3. Read the red error text in the terminal—often it names the problem (port, disk, permission).

---

## For developers and advanced options

- Full project details, building from source, and GitHub Actions: **[README.md](README.md)**

---

## Summary checklist

1. Install **Docker Desktop** and start it  
2. **Download ZIP** of the repo and unzip  
3. Open **terminal** in that folder  
4. Run **`docker compose -f docker-compose.prod.yml pull`**  
5. Run **`docker compose -f docker-compose.prod.yml up -d`**  
6. Run **`docker compose ... exec ollama ollama pull llama3.2`**  
7. Open **[http://localhost:8080](http://localhost:8080)** in your browser  

You are done.
