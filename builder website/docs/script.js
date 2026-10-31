const REPO = "nopaltavta/builderEngine";
const INSTALLER = "builder-engine-win-Setup.exe";
const API = `https://api.github.com/repos/${REPO}/releases/latest`;

const versionEl = document.getElementById("version");
const sizeEl = document.getElementById("size");
const downloadEl = document.getElementById("download");

function formatSize(bytes) {
  return `${Math.round(bytes / (1024 * 1024))} MB`;
}

async function loadLatestRelease() {
  try {
    const response = await fetch(API, {
      headers: { Accept: "application/vnd.github+json" },
    });

    if (!response.ok) return;

    const release = await response.json();
    const installer = (release.assets || []).find((asset) => asset.name === INSTALLER);

    if (release.tag_name) {
      versionEl.textContent = release.tag_name;
    }

    if (installer) {
      downloadEl.href = installer.browser_download_url;
      sizeEl.textContent = formatSize(installer.size);
    }
  } catch {
    // Keep the static fallback values already in the page.
  }
}

loadLatestRelease();
