import * as THREE from "three";
import { OrbitControls } from "three/addons/controls/OrbitControls.js";
import { GLTFLoader } from "three/addons/loaders/GLTFLoader.js";
import { FBXLoader } from "three/addons/loaders/FBXLoader.js";
import { DRACOLoader } from "three/addons/loaders/DRACOLoader.js";
import { MeshoptDecoder } from "three/addons/libs/meshopt_decoder.module.js";

const $ = (selector) => document.querySelector(selector);
const els = {
  viewport: $("#viewport"),
  empty: $("#empty-state"),
  sourceInput: $("#source-input"),
  sourceDrop: $("#source-drop"),
  sourceTitle: $("#source-title"),
  sourceMeta: $("#source-meta"),
  sourceStats: $("#source-stats"),
  statTriangles: $("#stat-triangles"),
  statMaterials: $("#stat-materials"),
  statDimensions: $("#stat-dimensions"),
  brickInput: $("#brick-input"),
  concreteInput: $("#concrete-input"),
  glassInput: $("#glass-input"),
  brickSize: $("#brick-size"),
  concreteSize: $("#concrete-size"),
  glassSize: $("#glass-size"),
  mappingSection: $("#mapping-section"),
  materialMap: $("#material-map"),
  buildButton: $("#build-button"),
  buildHint: $("#build-hint"),
  progress: $("#progress-overlay"),
  progressTitle: $("#progress-title"),
  progressText: $("#progress-text"),
  progressBar: $("#progress-bar"),
  viewSource: $("#view-source"),
  viewResult: $("#view-result"),
  frameButton: $("#frame-button"),
  gridButton: $("#grid-button"),
  wireButton: $("#wire-button"),
  resultStatus: $("#result-status"),
  resultCount: $("#result-count"),
  downloadJson: $("#download-json"),
  downloadFbx: $("#download-fbx"),
  density: $("#density"),
  densityOutput: $("#density-output"),
  toast: $("#toast"),
};

const state = {
  sourceFile: null,
  sourceRoot: null,
  sourceScene: null,
  sourceBounds: null,
  materialCatalog: [],
  materialMapping: new Map(),
  textureSamplers: new Map(),
  blocks: {
    brick: null,
    concrete: null,
    glass: null,
  },
  gridCells: [],
  cells: [],
  details: [],
  buildSettings: null,
  previewMeshes: [],
  wireframe: false,
  activeView: "source",
};

const TYPE_COLORS = {
  brick: 0xe35f42,
  concrete: 0xb8bec4,
  glass: 0x70d8e8,
  detail: 0x2e3339,
};

// Concrete and Glass keep the kit's common uniform scale. Brick uses its
// authored rectangular proportions and is laid as a continuous running bond.
// A tiny overlap hides the rounded/bevelled seams on the larger kit pieces.
const KIT_SEAM_OVERLAP = 1.035;

const scene = new THREE.Scene();
scene.background = new THREE.Color(0x0b0c0d);
const camera = new THREE.PerspectiveCamera(42, 1, 0.01, 5000);
camera.position.set(8, 6, 10);
const renderer = new THREE.WebGLRenderer({ antialias: true, powerPreference: "high-performance" });
renderer.setPixelRatio(Math.min(devicePixelRatio, 2));
renderer.outputColorSpace = THREE.SRGBColorSpace;
renderer.shadowMap.enabled = true;
els.viewport.prepend(renderer.domElement);

const controls = new OrbitControls(camera, renderer.domElement);
controls.enableDamping = true;
controls.dampingFactor = 0.08;
controls.screenSpacePanning = true;

scene.add(new THREE.HemisphereLight(0xcce4ff, 0x202125, 2.2));
const keyLight = new THREE.DirectionalLight(0xffffff, 3.4);
keyLight.position.set(7, 12, 9);
scene.add(keyLight);
const rimLight = new THREE.DirectionalLight(0xff7254, 1.4);
rimLight.position.set(-8, 5, -7);
scene.add(rimLight);

const grid = new THREE.GridHelper(100, 100, 0x42464b, 0x25282c);
grid.material.transparent = true;
grid.material.opacity = 0.65;
scene.add(grid);

const sourceDisplay = new THREE.Group();
sourceDisplay.name = "SourcePreview";
const resultDisplay = new THREE.Group();
resultDisplay.name = "ConvertedPreview";
resultDisplay.visible = false;
scene.add(sourceDisplay, resultDisplay);

new ResizeObserver(resizeRenderer).observe(els.viewport);
renderer.setAnimationLoop(() => {
  controls.update();
  renderer.render(scene, camera);
});

function resizeRenderer() {
  const width = Math.max(1, els.viewport.clientWidth);
  const height = Math.max(1, els.viewport.clientHeight);
  renderer.setSize(width, height, false);
  camera.aspect = width / height;
  camera.updateProjectionMatrix();
}

function formatNumber(value) {
  return new Intl.NumberFormat("en-US", { maximumFractionDigits: 0 }).format(value);
}

function formatSize(vector) {
  return `${trimNumber(vector.x)} × ${trimNumber(vector.y)} × ${trimNumber(vector.z)}`;
}

function trimNumber(value) {
  return Number(value.toFixed(value < 1 ? 3 : 2)).toString();
}

function fileStem(name) {
  return (name || "Building").replace(/\.[^.]+$/, "").replace(/[^a-z0-9_-]+/gi, "_");
}

function showToast(message, error = false) {
  els.toast.textContent = message;
  els.toast.classList.toggle("error", error);
  els.toast.classList.add("visible");
  clearTimeout(showToast.timer);
  showToast.timer = setTimeout(() => els.toast.classList.remove("visible"), 3800);
}

function showProgress(title, text, fraction = 0) {
  els.progressTitle.textContent = title;
  els.progressText.textContent = text;
  els.progressBar.style.width = `${THREE.MathUtils.clamp(fraction, 0, 1) * 100}%`;
  els.progress.classList.remove("hidden");
}

function hideProgress() {
  els.progress.classList.add("hidden");
}

function nextFrame() {
  return new Promise((resolve) => requestAnimationFrame(resolve));
}

function disposeObject(root) {
  if (!root) return;
  root.traverse((object) => {
    if (!object.isMesh) return;
    object.geometry?.dispose?.();
    const materials = Array.isArray(object.material) ? object.material : [object.material];
    materials.forEach((material) => material?.dispose?.());
  });
  root.removeFromParent();
}

function clearGroup(group, dispose = false) {
  while (group.children.length) {
    const child = group.children[0];
    if (dispose) disposeObject(child);
    else child.removeFromParent();
  }
}

function fitCamera(target = state.activeView === "result" ? resultDisplay : sourceDisplay) {
  const box = new THREE.Box3().setFromObject(target);
  if (box.isEmpty()) return;
  const sphere = box.getBoundingSphere(new THREE.Sphere());
  const distance = Math.max(2, sphere.radius / Math.sin(THREE.MathUtils.degToRad(camera.fov * 0.5)) * 1.12);
  const direction = new THREE.Vector3(1, 0.72, 1.15).normalize();
  camera.position.copy(sphere.center).addScaledVector(direction, distance);
  camera.near = Math.max(0.01, distance / 500);
  camera.far = Math.max(100, distance * 8);
  camera.updateProjectionMatrix();
  controls.target.copy(sphere.center);
  controls.update();
}

function applyWireframe() {
  [sourceDisplay, resultDisplay].forEach((root) => root.traverse((object) => {
    if (!object.isMesh) return;
    const materials = Array.isArray(object.material) ? object.material : [object.material];
    materials.forEach((material) => {
      if (material && "wireframe" in material) material.wireframe = state.wireframe;
    });
  }));
}

function setView(mode) {
  if (mode === "result" && !state.cells.length) return;
  state.activeView = mode;
  sourceDisplay.visible = mode === "source";
  resultDisplay.visible = mode === "result";
  els.viewSource.classList.toggle("active", mode === "source");
  els.viewResult.classList.toggle("active", mode === "result");
  fitCamera();
}

function readSettings() {
  const positive = (selector, fallback) => Math.max(0.001, Number($(selector).value) || fallback);
  const requestedCell = new THREE.Vector3(positive("#cell-x", 1), positive("#cell-y", 1), positive("#cell-z", 1));
  const cellUnit = Math.min(requestedCell.x, requestedCell.y, requestedCell.z);
  const cell = new THREE.Vector3(cellUnit, cellUnit, cellUnit);
  ["#cell-x", "#cell-y", "#cell-z"].forEach((selector) => { $(selector).value = trimNumber(cellUnit); });
  return {
    cell,
    gap: 0,
    budget: Math.max(100, Number($("#block-budget").value) || 6000),
    density: Number(els.density.value) || 1,
    detailKeywords: $("#detail-keywords").value.toLowerCase().split(/[,;\n]+/).map((x) => x.trim()).filter(Boolean),
    groundAlign: $("#ground-align").checked,
    fitAssets: false,
    keepDetails: $("#keep-details").checked,
  };
}

function hierarchyName(object) {
  const names = [];
  let cursor = object;
  while (cursor) {
    names.push(cursor.name || "");
    cursor = cursor.parent;
  }
  return names.reverse().join("/").toLowerCase();
}

function includesAny(value, tokens) {
  return tokens.some((token) => token && value.includes(token));
}

function materialColor(material) {
  const color = material?.color?.clone?.() || new THREE.Color(0.55, 0.55, 0.55);
  return `#${color.getHexString()}`;
}

function inferKind(material, extraName = "") {
  const name = `${extraName} ${material?.name || ""}`.toLowerCase();
  if (includesAny(name, ["door", "handle", "knob", "hinge", "sign", "pipe", "railing", "stair", "awning"])) return "detail";
  if (includesAny(name, ["glass", "window", "vitre", "transparent"])) return "glass";
  if (includesAny(name, ["brick", "orange", "terracotta", "masonry", "redwall"])) return "brick";
  if (includesAny(name, ["concrete", "cement", "stone", "roof", "wall", "gray", "grey"])) return "concrete";
  if (material && (material.transparent || material.opacity < 0.82 || material.transmission > 0.1)) return "glass";
  const color = material?.color || new THREE.Color(0.55, 0.55, 0.55);
  return classifyTextureColor({ r: color.r, g: color.g, b: color.b, a: material?.opacity ?? 1 }, material);
}

function roundedCellSize(value) {
  if (!Number.isFinite(value) || value <= 0) return 0.1;
  return Number(value.toPrecision(3));
}

function applySuggestedCellSize(bounds) {
  const size = bounds.getSize(new THREE.Vector3());
  const maxDimension = Math.max(size.x, size.y, size.z);
  let cell = maxDimension / 18;
  const budget = Math.max(100, Number($("#block-budget").value) || 6000);
  const estimatedShell = 2 * ((size.x * size.y) + (size.x * size.z) + (size.y * size.z)) / Math.max(cell * cell, 1e-8);
  const target = Math.min(2400, budget * 0.7);
  if (estimatedShell > target) cell *= Math.sqrt(estimatedShell / target);
  cell = roundedCellSize(Math.max(maxDimension / 48, cell, 0.005));
  ["#cell-x", "#cell-y", "#cell-z"].forEach((selector) => {
    $(selector).value = cell;
    $(selector).step = Math.max(0.001, roundedCellSize(cell / 10));
  });
  return cell;
}

function makeTextureSampler(material) {
  const texture = material?.map;
  const image = texture?.image || texture?.source?.data;
  const sourceWidth = image?.naturalWidth || image?.videoWidth || image?.width;
  const sourceHeight = image?.naturalHeight || image?.videoHeight || image?.height;
  if (!texture || !image || !sourceWidth || !sourceHeight) return null;
  try {
    const maxSide = 2048;
    const scale = Math.min(1, maxSide / Math.max(sourceWidth, sourceHeight));
    const width = Math.max(1, Math.round(sourceWidth * scale));
    const height = Math.max(1, Math.round(sourceHeight * scale));
    const canvas = document.createElement("canvas");
    canvas.width = width;
    canvas.height = height;
    const context = canvas.getContext("2d", { willReadFrequently: true });
    context.imageSmoothingEnabled = false;
    context.drawImage(image, 0, 0, width, height);
    texture.updateMatrix();
    return { texture, width, height, pixels: context.getImageData(0, 0, width, height).data };
  } catch (error) {
    console.warn(`Could not read texture for ${material.name || "material"}`, error);
    return null;
  }
}

function prepareTextureSamplers(materials) {
  state.textureSamplers.clear();
  materials.forEach((entry) => {
    const sampler = makeTextureSampler(entry.material);
    if (sampler) state.textureSamplers.set(entry.uuid, sampler);
  });
}

function sampleTextureColor(material, uv) {
  const sampler = state.textureSamplers.get(material?.uuid);
  if (!sampler || !uv) return null;
  const transformed = uv.clone();
  sampler.texture.transformUv(transformed);
  const x = THREE.MathUtils.clamp(Math.round(transformed.x * (sampler.width - 1)), 0, sampler.width - 1);
  const y = THREE.MathUtils.clamp(Math.round(transformed.y * (sampler.height - 1)), 0, sampler.height - 1);
  const offset = (y * sampler.width + x) * 4;
  const tint = material?.color || new THREE.Color(1, 1, 1);
  const opacity = material?.opacity ?? 1;
  return {
    r: sampler.pixels[offset] / 255 * tint.r,
    g: sampler.pixels[offset + 1] / 255 * tint.g,
    b: sampler.pixels[offset + 2] / 255 * tint.b,
    a: sampler.pixels[offset + 3] / 255 * opacity,
  };
}

function rgbToHsv({ r, g, b }) {
  const value = Math.max(r, g, b);
  const minimum = Math.min(r, g, b);
  const chroma = value - minimum;
  let hue = 0;
  if (chroma > 1e-6) {
    if (value === r) hue = ((g - b) / chroma) % 6;
    else if (value === g) hue = (b - r) / chroma + 2;
    else hue = (r - g) / chroma + 4;
    hue = ((hue / 6) + 1) % 1;
  }
  return { hue, saturation: value > 0 ? chroma / value : 0, value, chroma };
}

function classifyTextureColor(color, material) {
  if (!color) return "detail";
  const hsv = rgbToHsv(color);
  const transmissive = (material?.transmission || 0) > 0.08 || material?.transparent || color.a < 0.72;
  const clearGlass = hsv.hue >= (145 / 360)
    && hsv.hue <= (230 / 360)
    && hsv.saturation >= 0.28
    && hsv.value >= 0.2
    && hsv.chroma >= 0.12;
  if (transmissive || clearGlass) return "glass";
  const clearOrange = (hsv.hue <= (26 / 360) || hsv.hue >= (350 / 360))
    && hsv.saturation >= 0.35
    && hsv.value >= 0.22
    && (color.r - color.g) >= 0.18
    && color.r > color.b * 1.2;
  if (clearOrange) return "brick";
  if (hsv.value >= 0.18 && hsv.saturation <= 0.24 && hsv.chroma <= 0.12) return "concrete";
  return "detail";
}

function textureUvAttribute(geometry, material) {
  const channel = Math.max(0, material?.map?.channel || 0);
  return geometry.getAttribute(channel === 0 ? "uv" : `uv${channel}`) || geometry.getAttribute("uv");
}

function refreshBuildAvailability() {
  const assetsReady = Object.values(state.blocks).every(Boolean);
  const ready = Boolean(state.sourceScene && assetsReady);
  els.buildButton.disabled = !ready;
  els.buildHint.textContent = ready
    ? "Review material mapping, then build."
    : state.sourceScene ? "Waiting for all three block assets…" : "Upload a GLB and wait for the block kit.";
}

async function loadBlockAsset(kind, buffer, fileName) {
  const loader = new FBXLoader();
  const resourcePath = kind === "brick" && fileName === "Brick.fbx" ? "./assets/Brick.fbm/" : "./assets/";
  const object = loader.parse(buffer, resourcePath);
  object.name = `${kind[0].toUpperCase()}${kind.slice(1)}Asset`;
  object.updateMatrixWorld(true);
  const bounds = new THREE.Box3().setFromObject(object);
  if (bounds.isEmpty()) throw new Error(`${fileName} has no visible mesh.`);
  const size = bounds.getSize(new THREE.Vector3());
  const center = bounds.getCenter(new THREE.Vector3());
  const thinAxis = size.x <= size.y && size.x <= size.z ? 0 : size.y <= size.z ? 1 : 2;
  state.blocks[kind] = { object, size, center, thinAxis, fileName };
  els[`${kind}Size`].textContent = `${formatSize(size)} · ${fileName}`;
  document.querySelector(`[data-asset="${kind}"]`).classList.remove("error");
  refreshBuildAvailability();
}

async function loadDefaultBlockAssets() {
  const defaults = {
    brick: "./assets/Brick.fbx",
    concrete: "./assets/concrete.fbx",
    glass: "./assets/Glass.fbx",
  };
  await Promise.all(Object.entries(defaults).map(async ([kind, url]) => {
    try {
      const response = await fetch(url);
      if (!response.ok) throw new Error(`HTTP ${response.status}`);
      await loadBlockAsset(kind, await response.arrayBuffer(), url.split("/").pop());
    } catch (error) {
      console.error(error);
      els[`${kind}Size`].textContent = "Default unavailable — choose FBX";
      document.querySelector(`[data-asset="${kind}"]`).classList.add("error");
    }
  }));
}

async function replaceBlockAsset(kind, file) {
  if (!file) return;
  try {
    showProgress("Loading block asset…", file.name, 0.35);
    await loadBlockAsset(kind, await file.arrayBuffer(), file.name);
    showToast(`${kind} asset replaced with ${file.name}.`);
  } catch (error) {
    showToast(error.message, true);
  } finally {
    hideProgress();
  }
}

function normalizeSource() {
  if (!state.sourceRoot || !state.sourceScene) return;
  state.sourceRoot.position.set(0, 0, 0);
  state.sourceRoot.updateMatrixWorld(true);
  const rawBounds = new THREE.Box3().setFromObject(state.sourceScene);
  if (rawBounds.isEmpty()) return;
  if ($("#ground-align").checked) {
    const center = rawBounds.getCenter(new THREE.Vector3());
    state.sourceRoot.position.set(-center.x, -rawBounds.min.y, -center.z);
  }
  state.sourceRoot.updateMatrixWorld(true);
  state.sourceBounds = new THREE.Box3().setFromObject(state.sourceRoot);
}

async function loadSourceFile(file) {
  if (!file) return;
  if (!file.name.toLowerCase().endsWith(".glb")) {
    showToast("Please choose a binary .glb file.", true);
    return;
  }
  try {
    showProgress("Importing GLB…", "Reading geometry and embedded materials", 0.08);
    const buffer = await file.arrayBuffer();
    const dracoLoader = new DRACOLoader();
    dracoLoader.setDecoderPath("https://www.gstatic.com/draco/versioned/decoders/1.5.7/");
    const gltfLoader = new GLTFLoader();
    gltfLoader.setDRACOLoader(dracoLoader);
    gltfLoader.setMeshoptDecoder(MeshoptDecoder);
    const gltf = await new Promise((resolve, reject) => gltfLoader.parse(buffer, "", resolve, reject));
    dracoLoader.dispose();
    showProgress("Analyzing model…", "Collecting meshes, materials and dimensions", 0.58);
    await nextFrame();

    clearGroup(sourceDisplay, true);
    clearResult();
    state.sourceFile = file;
    state.sourceScene = gltf.scene;
    state.sourceRoot = new THREE.Group();
    state.sourceRoot.name = "NormalizedSource";
    state.sourceRoot.add(state.sourceScene);
    sourceDisplay.add(state.sourceRoot);
    normalizeSource();

    const analysis = analyzeSource(state.sourceScene);
    state.materialCatalog = analysis.materials;
    state.materialMapping.clear();
    prepareTextureSamplers(analysis.materials);
    analysis.materials.forEach((entry) => {
      entry.suggestion = state.textureSamplers.has(entry.uuid) ? "auto" : entry.suggestion;
      state.materialMapping.set(entry.uuid, entry.suggestion);
    });
    renderMaterialMapping();

    const suggestedCell = applySuggestedCellSize(state.sourceBounds);

    els.sourceTitle.textContent = file.name;
    els.sourceMeta.textContent = `${(file.size / 1024 / 1024).toFixed(2)} MB · auto cell ${trimNumber(suggestedCell)}`;
    els.sourceDrop.classList.add("loaded");
    els.sourceStats.classList.remove("hidden");
    els.statTriangles.textContent = formatNumber(analysis.triangles);
    els.statMaterials.textContent = analysis.materials.length;
    els.statDimensions.textContent = formatSize(state.sourceBounds.getSize(new THREE.Vector3()));
    els.empty.classList.add("hidden");
    state.activeView = "source";
    setView("source");
    refreshBuildAvailability();
    applyWireframe();
    showToast("GLB ready. Review the material mapping before building.");
  } catch (error) {
    console.error(error);
    showToast(`Could not load GLB: ${error.message}`, true);
  } finally {
    els.sourceInput.value = "";
    hideProgress();
  }
}

function analyzeSource(root) {
  let triangles = 0;
  const materials = new Map();
  root.traverse((object) => {
    if (!object.isMesh || !object.geometry) return;
    const geometry = object.geometry;
    triangles += Math.floor((geometry.index ? geometry.index.count : geometry.attributes.position?.count || 0) / 3);
    const objectMaterials = Array.isArray(object.material) ? object.material : [object.material];
    objectMaterials.forEach((material, materialIndex) => {
      if (!material || materials.has(material.uuid)) return;
      materials.set(material.uuid, {
        uuid: material.uuid,
        name: material.name || `${object.name || "Mesh"} material ${materialIndex + 1}`,
        color: materialColor(material),
        suggestion: inferKind(material, object.name),
        material,
      });
    });
  });
  return { triangles, materials: [...materials.values()] };
}

function renderMaterialMapping() {
  els.materialMap.replaceChildren();
  state.materialCatalog.forEach((entry) => {
    const row = document.createElement("div");
    row.className = "material-row";
    const swatch = document.createElement("span");
    swatch.className = "material-color";
    swatch.style.background = entry.color;
    const name = document.createElement("span");
    name.className = "material-name";
    name.textContent = entry.name;
    name.title = entry.name;
    const select = document.createElement("select");
    ["auto", "brick", "concrete", "glass", "detail", "ignore"].forEach((kind) => {
      const option = document.createElement("option");
      option.value = kind;
      option.textContent = kind === "auto" ? "Auto (texture)" : kind[0].toUpperCase() + kind.slice(1);
      option.selected = state.materialMapping.get(entry.uuid) === kind;
      select.append(option);
    });
    select.addEventListener("change", () => state.materialMapping.set(entry.uuid, select.value));
    row.append(swatch, name, select);
    els.materialMap.append(row);
  });
  els.mappingSection.classList.toggle("hidden", state.materialCatalog.length === 0);
}

function clearResult() {
  clearGroup(resultDisplay, true);
  state.previewMeshes = [];
  state.gridCells = [];
  state.cells = [];
  state.details = [];
  state.buildSettings = null;
  els.viewResult.disabled = true;
  els.downloadJson.disabled = true;
  els.downloadFbx.disabled = true;
  els.resultStatus.textContent = "No structure generated";
  els.resultCount.textContent = "0 blocks";
}

function getGeometryGroups(mesh) {
  const geometry = mesh.geometry;
  const count = geometry.index ? geometry.index.count : geometry.attributes.position.count;
  if (geometry.groups.length) return geometry.groups;
  return [{ start: 0, count, materialIndex: 0 }];
}

function cellKey(x, y, z) {
  return `${x},${y},${z}`;
}

function snapFace(normal) {
  const absolute = [Math.abs(normal.x), Math.abs(normal.y), Math.abs(normal.z)];
  const axis = absolute.indexOf(Math.max(...absolute));
  const face = new THREE.Vector3();
  face.setComponent(axis, Math.sign(normal.getComponent(axis)) || 1);
  return face;
}

function faceIndex(face) {
  const axis = Math.abs(face.x) > 0.5 ? 0 : Math.abs(face.y) > 0.5 ? 1 : 2;
  return axis * 2 + (face.getComponent(axis) < 0 ? 1 : 0);
}

function addCell(map, point, origin, settings, kind, face, weight = 1) {
  const epsilon = Math.min(settings.cell.x, settings.cell.y, settings.cell.z) * 1e-4;
  const insidePoint = point.clone().addScaledVector(face, -epsilon);
  const x = Math.floor((insidePoint.x - origin.x) / settings.cell.x);
  const y = Math.floor((insidePoint.y - origin.y) / settings.cell.y);
  const z = Math.floor((insidePoint.z - origin.z) / settings.cell.z);
  const key = cellKey(x, y, z);
  let cell = map.get(key);
  if (!cell) {
    cell = {
      coordinate: [x, y, z],
      faceVotes: new Float64Array(6),
      materialVotes: Array.from({ length: 6 }, () => ({ brick: 0, concrete: 0, glass: 0 })),
    };
    map.set(key, cell);
  }
  const index = faceIndex(face);
  cell.materialVotes[index][kind] += weight;
  cell.faceVotes[index] += weight;
}

function classifyTriangleKind(classification) {
  if (classification.kind !== "auto") return classification.kind;
  if (!classification.uvA || !classification.uvB || !classification.uvC) return inferKind(classification.material);
  const samples = [
    [1 / 3, 1 / 3, 1 / 3],
    [0.8, 0.1, 0.1],
    [0.1, 0.8, 0.1],
    [0.1, 0.1, 0.8],
    [0.45, 0.45, 0.1],
    [0.45, 0.1, 0.45],
    [0.1, 0.45, 0.45],
  ];
  const votes = { brick: 0, concrete: 0, glass: 0, detail: 0 };
  samples.forEach(([u, v, w]) => {
    const uv = new THREE.Vector2(
      classification.uvA.x * w + classification.uvB.x * u + classification.uvC.x * v,
      classification.uvA.y * w + classification.uvB.y * u + classification.uvC.y * v,
    );
    votes[classifyTextureColor(sampleTextureColor(classification.material, uv), classification.material)]++;
  });
  const [winner, count] = Object.entries(votes).sort((a, b) => b[1] - a[1])[0];
  return count >= 4 ? winner : "detail";
}

function sampleTriangle(map, a, b, c, origin, settings, kind) {
  const ab = b.distanceTo(a);
  const bc = c.distanceTo(b);
  const ca = a.distanceTo(c);
  const step = Math.max(0.001, Math.min(settings.cell.x, settings.cell.y, settings.cell.z) / settings.density);
  const subdivisions = THREE.MathUtils.clamp(Math.ceil(Math.max(ab, bc, ca) / step), 1, 32);
  const normal = new THREE.Vector3().crossVectors(new THREE.Vector3().subVectors(b, a), new THREE.Vector3().subVectors(c, a)).normalize();
  if (!Number.isFinite(normal.x)) return;
  const face = snapFace(normal);
  const area = new THREE.Triangle(a, b, c).getArea();
  const sampleWeight = area / Math.max(1, ((subdivisions + 1) * (subdivisions + 2)) / 2);
  for (let i = 0; i <= subdivisions; i++) {
    for (let j = 0; j <= subdivisions - i; j++) {
      const u = i / subdivisions;
      const v = j / subdivisions;
      const w = 1 - u - v;
      const point = new THREE.Vector3(
        a.x * w + b.x * u + c.x * v,
        a.y * w + b.y * u + c.y * v,
        a.z * w + b.z * u + c.z * v,
      );
      addCell(map, point, origin, settings, kind, face, sampleWeight);
    }
  }
  addCell(map, new THREE.Vector3().addVectors(a, b).add(c).multiplyScalar(1 / 3), origin, settings, kind, face, sampleWeight);
}

function markDetailTriangleCells(detailFaceWeights, a, b, c, origin, settings) {
  const ab = b.distanceTo(a);
  const bc = c.distanceTo(b);
  const ca = a.distanceTo(c);
  const step = Math.max(0.001, Math.min(settings.cell.x, settings.cell.y, settings.cell.z) / settings.density);
  const subdivisions = THREE.MathUtils.clamp(Math.ceil(Math.max(ab, bc, ca) / step), 1, 32);
  const normal = new THREE.Vector3().crossVectors(new THREE.Vector3().subVectors(b, a), new THREE.Vector3().subVectors(c, a)).normalize();
  if (!Number.isFinite(normal.x)) return;
  const face = snapFace(normal);
  const area = new THREE.Triangle(a, b, c).getArea();
  const sampleCount = Math.max(1, ((subdivisions + 1) * (subdivisions + 2)) / 2);
  const sampleWeight = area / sampleCount;
  const epsilon = Math.min(settings.cell.x, settings.cell.y, settings.cell.z) * 1e-4;
  const addDetailWeight = (point) => {
    const insidePoint = point.clone().addScaledVector(face, -epsilon);
    const x = Math.floor((insidePoint.x - origin.x) / settings.cell.x);
    const y = Math.floor((insidePoint.y - origin.y) / settings.cell.y);
    const z = Math.floor((insidePoint.z - origin.z) / settings.cell.z);
    const key = `${cellKey(x, y, z)}|${faceIndex(face)}`;
    detailFaceWeights.set(key, (detailFaceWeights.get(key) || 0) + sampleWeight);
  };
  for (let i = 0; i <= subdivisions; i++) {
    for (let j = 0; j <= subdivisions - i; j++) {
      const u = i / subdivisions;
      const v = j / subdivisions;
      const w = 1 - u - v;
      const point = new THREE.Vector3(
        a.x * w + b.x * u + c.x * v,
        a.y * w + b.y * u + c.y * v,
        a.z * w + b.z * u + c.z * v,
      );
      addDetailWeight(point);
    }
  }
  addDetailWeight(new THREE.Vector3().addVectors(a, b).add(c).multiplyScalar(1 / 3));
}

function markDetailGroupCells(mesh, group, detailFaceWeights, origin, settings) {
  const geometry = mesh.geometry;
  const position = geometry.attributes.position;
  const index = geometry.index;
  const end = Math.min(group.start + group.count, index ? index.count : position.count);
  const a = new THREE.Vector3();
  const b = new THREE.Vector3();
  const c = new THREE.Vector3();
  for (let cursor = group.start; cursor + 2 < end; cursor += 3) {
    const ia = index ? index.getX(cursor) : cursor;
    const ib = index ? index.getX(cursor + 1) : cursor + 1;
    const ic = index ? index.getX(cursor + 2) : cursor + 2;
    a.fromBufferAttribute(position, ia).applyMatrix4(mesh.matrixWorld);
    b.fromBufferAttribute(position, ib).applyMatrix4(mesh.matrixWorld);
    c.fromBufferAttribute(position, ic).applyMatrix4(mesh.matrixWorld);
    markDetailTriangleCells(detailFaceWeights, a, b, c, origin, settings);
  }
}

function faceVector(index) {
  const axis = Math.floor(index / 2);
  const face = new THREE.Vector3();
  face.setComponent(axis, index % 2 ? -1 : 1);
  return face;
}

function resolveCellKind(votes) {
  return Object.entries(votes).sort((a, b) => b[1] - a[1])[0][0];
}

function resolveSurfaceEnvelope(cellMap, detailFaceWeights, origin, settings) {
  const sites = new Map();
  for (const [key, cell] of cellMap) {
    for (let face = 0; face < 6; face++) {
      const structuralWeight = cell.faceVotes[face];
      if (structuralWeight <= 0) continue;
      sites.set(`${key}|${face}`, {
        coordinate: [...cell.coordinate],
        face,
        votes: cell.materialVotes[face],
        structuralWeight,
        detailWeight: 0,
      });
    }
  }
  for (const [key, detailWeight] of detailFaceWeights) {
    let site = sites.get(key);
    if (!site) {
      const [coordinateKey, faceText] = key.split("|");
      site = {
        coordinate: coordinateKey.split(",").map(Number),
        face: Number(faceText),
        votes: { brick: 0, concrete: 0, glass: 0 },
        structuralWeight: 0,
        detailWeight: 0,
      };
      sites.set(key, site);
    }
    site.detailWeight += detailWeight;
  }

  // Meshy-style GLBs contain bevels, recesses and inner surfaces. Keep only
  // the first visible depth for every signed face ray instead of instancing all
  // those layers on top of each other.
  const rayForSite = (site) => {
    const normalAxis = Math.floor(site.face / 2);
    const tangentAxes = [0, 1, 2].filter((axis) => axis !== normalAxis);
    return `${site.face}|${site.coordinate[tangentAxes[0]]}|${site.coordinate[tangentAxes[1]]}`;
  };
  const depthForSite = (site) => {
    const normalAxis = Math.floor(site.face / 2);
    return site.coordinate[normalAxis] * (site.face % 2 ? -1 : 1);
  };
  const structuralByRay = new Map();
  const detailsByRay = new Map();
  for (const site of sites.values()) {
    const rayKey = rayForSite(site);
    const depth = depthForSite(site);
    if (site.structuralWeight > 0) {
      const current = structuralByRay.get(rayKey);
      if (!current || depth > current.depth) structuralByRay.set(rayKey, { site, depth });
    }
    if (site.detailWeight > 0) {
      if (!detailsByRay.has(rayKey)) detailsByRay.set(rayKey, []);
      detailsByRay.get(rayKey).push({ depth, weight: site.detailWeight });
    }
  }

  const candidates = [];
  const protectedDetailRays = new Set();
  for (const [rayKey, { site, depth }] of structuralByRay) {
    // Detail geometry may sit slightly in front of the wall. Aggregate its
    // coverage at or in front of the chosen structural surface, then compare
    // weights. A single tiny sample can no longer erase a whole logical cell.
    const projectedDetailWeight = (detailsByRay.get(rayKey) || [])
      .filter((detail) => detail.depth >= depth)
      .reduce((sum, detail) => sum + detail.weight, 0);
    if (projectedDetailWeight > site.structuralWeight) {
      protectedDetailRays.add(rayKey);
      continue;
    }
    const kind = resolveCellKind(site.votes);
    const total = Object.values(site.votes).reduce((sum, value) => sum + value, 0);
    const [x, y, z] = site.coordinate;
    candidates.push({
      kind,
      coordinate: [x, y, z],
      position: new THREE.Vector3(
        origin.x + (x + 0.5) * settings.cell.x,
        origin.y + (y + 0.5) * settings.cell.y,
        origin.z + (z + 0.5) * settings.cell.z,
      ),
      normal: faceVector(site.face),
      confidence: total > 0 ? site.votes[kind] / total : 0,
      surfaceWeight: site.structuralWeight,
      rayKey,
    });
  }
  const tangentCellArea = Math.min(
    settings.cell.x * settings.cell.y,
    settings.cell.x * settings.cell.z,
    settings.cell.y * settings.cell.z,
  );
  for (const [rayKey, detailSites] of detailsByRay) {
    const totalDetailWeight = detailSites.reduce((sum, detail) => sum + detail.weight, 0);
    if (totalDetailWeight >= tangentCellArea * 0.35) protectedDetailRays.add(rayKey);
  }

  // A corner may be reached by two or three face rays. One physical asset owns
  // that logical cell. Prefer a full Concrete cube at mixed-face corners;
  // otherwise keep the strongest sampled surface. This avoids both duplicates
  // and open corner seams caused by letting one thin panel own the voxel.
  const candidatesByCoordinate = new Map();
  for (const candidate of candidates) {
    const key = cellKey(...candidate.coordinate);
    if (!candidatesByCoordinate.has(key)) candidatesByCoordinate.set(key, []);
    candidatesByCoordinate.get(key).push(candidate);
  }
  const byCoordinate = new Map();
  for (const [key, coordinateCandidates] of candidatesByCoordinate) {
    const normalAxes = new Set(coordinateCandidates.map((candidate) => Math.floor(faceIndex(candidate.normal) / 2)));
    const concreteCorner = normalAxes.size > 1
      ? coordinateCandidates.find((candidate) => candidate.kind === "concrete")
      : null;
    const winner = concreteCorner || [...coordinateCandidates].sort((a, b) => (
      b.surfaceWeight * (0.75 + b.confidence * 0.25)
      - a.surfaceWeight * (0.75 + a.confidence * 0.25)
    ))[0];
    const secondaryFaces = concreteCorner ? [] : coordinateCandidates
      .filter((candidate) => candidate !== winner
        && faceIndex(candidate.normal) !== faceIndex(winner.normal)
        && candidate.kind !== "concrete")
      .map((candidate) => ({
        kind: candidate.kind,
        normal: candidate.normal.clone(),
        confidence: candidate.confidence,
        surfaceWeight: candidate.surfaceWeight,
      }));
    byCoordinate.set(key, { ...winner, secondaryFaces });
  }
  return {
    cells: [...byCoordinate.values()].map((cell, id) => ({ ...cell, id })),
    protectedDetailRays,
  };
}

function axisVector(axis) {
  return axis === 0 ? new THREE.Vector3(1, 0, 0) : axis === 1 ? new THREE.Vector3(0, 1, 0) : new THREE.Vector3(0, 0, 1);
}

function orientationFor(kind, normal) {
  const asset = state.blocks[kind];
  if (!asset || normal.lengthSq() < 0.00001) return new THREE.Quaternion();
  const face = snapFace(normal);
  const alignThin = new THREE.Quaternion().setFromUnitVectors(axisVector(asset.thinAxis), face);
  const rotatedLong = axisVector(assetLongAxis(asset)).applyQuaternion(alignThin).projectOnPlane(face).normalize();
  const targetLong = Math.abs(face.y) > 0.5
    ? new THREE.Vector3(1, 0, 0)
    : new THREE.Vector3(face.z, 0, -face.x).normalize();
  if (rotatedLong.lengthSq() < 1e-8) return alignThin;
  const cross = new THREE.Vector3().crossVectors(rotatedLong, targetLong);
  const angle = Math.atan2(face.dot(cross), THREE.MathUtils.clamp(rotatedLong.dot(targetLong), -1, 1));
  return new THREE.Quaternion().setFromAxisAngle(face, angle).multiply(alignThin);
}

function commonKitScale(settings) {
  const concrete = state.blocks.concrete;
  if (!concrete) return 1;
  const cellUnit = Math.max(1e-8, Math.min(settings.cell.x, settings.cell.y, settings.cell.z));
  const concreteReference = Math.max(concrete.size.x, concrete.size.y, concrete.size.z, 1e-8);
  return cellUnit / concreteReference;
}

function brickCourseMetrics(settings) {
  const brick = state.blocks.brick;
  const cellUnit = Math.max(1e-8, Math.min(settings.cell.x, settings.cell.y, settings.cell.z));
  if (!brick) return { rowsPerCell: 1, uniformScale: 1, length: cellUnit, height: cellUnit, thickness: cellUnit };
  const longAxis = assetLongAxis(brick);
  const shortAxis = assetShortAxis(brick);
  const authoredShort = Math.max(brick.size.getComponent(shortAxis) * commonKitScale(settings), 1e-8);
  const rowsPerCell = Math.max(1, Math.round(cellUnit / authoredShort));
  const height = cellUnit / rowsPerCell;
  const uniformScale = height / Math.max(brick.size.getComponent(shortAxis), 1e-8);
  return {
    rowsPerCell,
    uniformScale,
    length: brick.size.getComponent(longAxis) * uniformScale,
    height,
    thickness: brick.size.getComponent(brick.thinAxis) * uniformScale,
  };
}

function fittedScale(kind, settings) {
  if (kind === "brick" && state.blocks.brick) {
    const scalar = brickCourseMetrics(settings).uniformScale;
    return new THREE.Vector3(scalar, scalar, scalar);
  }
  const commonScalar = commonKitScale(settings) * KIT_SEAM_OVERLAP;
  return new THREE.Vector3(commonScalar, commonScalar, commonScalar);
}

function extractDetailPrimitive(mesh, group, material) {
  const source = mesh.geometry;
  const index = source.index;
  const attributes = ["position", "normal", "uv", "uv1", "color"];
  const target = new THREE.BufferGeometry();
  for (const name of attributes) {
    const attribute = source.getAttribute(name);
    if (!attribute) continue;
    const output = new Float32Array(group.count * attribute.itemSize);
    for (let i = 0; i < group.count; i++) {
      const sourceIndex = index ? index.getX(group.start + i) : group.start + i;
      for (let component = 0; component < attribute.itemSize; component++) {
        output[i * attribute.itemSize + component] = attribute.array[sourceIndex * attribute.itemSize + component];
      }
    }
    target.setAttribute(name, new THREE.BufferAttribute(output, attribute.itemSize, attribute.normalized));
  }
  if (!target.getAttribute("normal")) target.computeVertexNormals();
  target.computeBoundingBox();
  const detail = new THREE.Mesh(target, material?.clone?.() || material);
  detail.name = `${mesh.name || "Detail"}_${material?.name || "Part"}`;
  detail.matrix.copy(mesh.matrixWorld);
  detail.matrixAutoUpdate = false;
  detail.userData.SmashType = "Detail";
  return detail;
}

function extractDetailTriangles(mesh, triangles, material, componentIndex = 0) {
  const source = mesh.geometry;
  const attributes = ["position", "normal", "uv", "uv1", "color"];
  const target = new THREE.BufferGeometry();
  const sourceIndices = triangles.flat();
  for (const name of attributes) {
    const attribute = source.getAttribute(name);
    if (!attribute) continue;
    const output = new Float32Array(sourceIndices.length * attribute.itemSize);
    sourceIndices.forEach((sourceIndex, outputIndex) => {
      for (let component = 0; component < attribute.itemSize; component++) {
        output[outputIndex * attribute.itemSize + component] = attribute.array[sourceIndex * attribute.itemSize + component];
      }
    });
    target.setAttribute(name, new THREE.BufferAttribute(output, attribute.itemSize, attribute.normalized));
  }
  if (!target.getAttribute("normal")) target.computeVertexNormals();
  target.computeBoundingBox();
  const detail = new THREE.Mesh(target, material?.clone?.() || material);
  detail.name = `${mesh.name || "Mesh"}_${material?.name || "Texture"}_Detail_${String(componentIndex).padStart(3, "0")}`;
  detail.matrix.copy(mesh.matrixWorld);
  detail.matrixAutoUpdate = false;
  detail.userData.SmashType = "Detail";
  return detail;
}

function splitTriangleComponents(triangles) {
  if (triangles.length < 2) return triangles.length ? [triangles] : [];
  const parents = triangles.map((_, index) => index);
  const find = (value) => {
    while (parents[value] !== value) {
      parents[value] = parents[parents[value]];
      value = parents[value];
    }
    return value;
  };
  const unite = (a, b) => {
    const rootA = find(a);
    const rootB = find(b);
    if (rootA !== rootB) parents[rootB] = rootA;
  };
  const edges = new Map();
  triangles.forEach((triangle, triangleIndex) => {
    for (const [a, b] of [[triangle[0], triangle[1]], [triangle[1], triangle[2]], [triangle[2], triangle[0]]]) {
      const key = a < b ? `${a},${b}` : `${b},${a}`;
      const neighbor = edges.get(key);
      if (neighbor === undefined) edges.set(key, triangleIndex);
      else unite(triangleIndex, neighbor);
    }
  });
  const groups = new Map();
  triangles.forEach((triangle, index) => {
    const root = find(index);
    if (!groups.has(root)) groups.set(root, []);
    groups.get(root).push(triangle);
  });
  return [...groups.values()];
}

function sameFace(a, b) {
  return a.normal.dot(b.normal) > 0.99;
}

function surfaceNeighborAxes(normal) {
  const face = snapFace(normal);
  if (Math.abs(face.x) > 0.5) return [1, 2];
  if (Math.abs(face.y) > 0.5) return [0, 2];
  return [0, 1];
}

function cleanMaterialRegions(cells) {
  let current = cells.map((cell) => ({ ...cell }));
  for (let pass = 0; pass < 2; pass++) {
    const lookup = new Map(current.map((cell) => [cellKey(...cell.coordinate), cell]));
    current = current.map((cell) => {
      const counts = { brick: 0, concrete: 0, glass: 0 };
      const [axisA, axisB] = surfaceNeighborAxes(cell.normal);
      for (const [axis, direction] of [[axisA, -1], [axisA, 1], [axisB, -1], [axisB, 1]]) {
        const coordinate = [...cell.coordinate];
        coordinate[axis] += direction;
        const neighbor = lookup.get(cellKey(...coordinate));
        if (neighbor && sameFace(cell, neighbor)) counts[neighbor.kind]++;
      }
      const [majority, count] = Object.entries(counts).sort((a, b) => b[1] - a[1])[0];
      const ownNeighbors = counts[cell.kind];
      if (count >= 3 && majority !== cell.kind && (cell.confidence < 0.82 || ownNeighbors === 0)) {
        return { ...cell, kind: majority, confidence: Math.max(cell.confidence, count / 4) };
      }
      return cell;
    });
  }
  return current;
}

function regularizeSurfaceCells(cells, protectedDetailRays, origin, settings) {
  let current = cells.map((cell) => ({ ...cell }));
  const rayKeyFor = (coordinate, normal) => {
    const face = faceIndex(snapFace(normal));
    const normalAxis = Math.floor(face / 2);
    const tangentAxes = [0, 1, 2].filter((axis) => axis !== normalAxis);
    return `${face}|${coordinate[tangentAxes[0]]}|${coordinate[tangentAxes[1]]}`;
  };
  for (let pass = 0; pass < 2; pass++) {
    const lookup = new Map(current.map((cell) => [cellKey(...cell.coordinate), cell]));
    const additions = [];
    const candidateCoordinates = new Map();
    current.forEach((cell) => {
      const [axisA, axisB] = surfaceNeighborAxes(cell.normal);
      for (const [axis, direction] of [[axisA, -1], [axisA, 1], [axisB, -1], [axisB, 1]]) {
        const coordinate = [...cell.coordinate];
        coordinate[axis] += direction;
        const key = cellKey(...coordinate);
        if (!lookup.has(key) && !candidateCoordinates.has(key)) candidateCoordinates.set(key, coordinate);
      }
    });
    for (const coordinate of candidateCoordinates.values()) {
      const faceGroups = new Map();
      for (let face = 0; face < 6; face++) {
        const normal = faceVector(face);
        const [axisA, axisB] = surfaceNeighborAxes(normal);
        const group = [];
        for (const [axis, direction] of [[axisA, -1], [axisA, 1], [axisB, -1], [axisB, 1]]) {
          const adjacent = [...coordinate];
          adjacent[axis] += direction;
          const neighbor = lookup.get(cellKey(...adjacent));
          if (neighbor && faceIndex(snapFace(neighbor.normal)) === face) group.push(neighbor);
        }
        if (group.length) faceGroups.set(face, group);
      }
      const group = [...faceGroups.values()].sort((a, b) => b.length - a.length)[0] || [];
      if (group.length < 3) continue;
      const normal = group[0].normal.clone();
      if (protectedDetailRays.has(rayKeyFor(coordinate, normal))) continue;
      const kindCounts = { brick: 0, concrete: 0, glass: 0 };
      group.forEach((neighbor) => kindCounts[neighbor.kind]++);
      const kind = Object.entries(kindCounts).sort((a, b) => b[1] - a[1])[0][0];
      const [x, y, z] = coordinate;
      additions.push({
        id: -1,
        kind,
        coordinate,
        position: new THREE.Vector3(
          origin.x + (x + 0.5) * settings.cell.x,
          origin.y + (y + 0.5) * settings.cell.y,
          origin.z + (z + 0.5) * settings.cell.z,
        ),
        normal,
        confidence: 0.75,
        surfaceWeight: Math.min(...group.map((neighbor) => neighbor.surfaceWeight || 1)),
      });
    }
    if (!additions.length) break;
    current.push(...additions);
  }

  // Remove lone raster specks. A cell that belongs to a real wall/roof keeps
  // at least one tangent neighbor with the same snapped face.
  const lookup = new Map(current.map((cell) => [cellKey(...cell.coordinate), cell]));
  current = current.filter((cell) => {
    const [axisA, axisB] = surfaceNeighborAxes(cell.normal);
    return [[axisA, -1], [axisA, 1], [axisB, -1], [axisB, 1]].some(([axis, direction]) => {
      const coordinate = [...cell.coordinate];
      coordinate[axis] += direction;
      const neighbor = lookup.get(cellKey(...coordinate));
      return neighbor && sameFace(cell, neighbor);
    });
  });
  return current.map((cell, id) => ({ ...cell, id }));
}

function assetLongAxis(asset) {
  let axis = 0;
  for (let candidate = 1; candidate < 3; candidate++) {
    if (candidate !== asset.thinAxis && (axis === asset.thinAxis || asset.size.getComponent(candidate) > asset.size.getComponent(axis))) axis = candidate;
  }
  return axis;
}

function assetShortAxis(asset) {
  return [0, 1, 2].find((axis) => axis !== asset.thinAxis && axis !== assetLongAxis(asset));
}

function dominantWorldAxis(localAxis, quaternion) {
  const vector = axisVector(localAxis).applyQuaternion(quaternion);
  const components = [Math.abs(vector.x), Math.abs(vector.y), Math.abs(vector.z)];
  return components.indexOf(Math.max(...components));
}

function flushPosition(cell, kind, settings) {
  const asset = state.blocks[kind];
  const face = snapFace(cell.normal);
  const normalAxis = Math.abs(face.x) > 0.5 ? 0 : Math.abs(face.y) > 0.5 ? 1 : 2;
  const cellThickness = settings.cell.getComponent(normalAxis);
  const assetThickness = asset.size.getComponent(asset.thinAxis) * fittedScale(kind, settings).getComponent(asset.thinAxis);
  return cell.position.clone().addScaledVector(face, (cellThickness - assetThickness) * 0.5);
}

function tileStructuralCells(gridCells, settings) {
  const placements = [];
  const ordered = [...gridCells].sort((a, b) => a.coordinate[1] - b.coordinate[1]
    || a.coordinate[2] - b.coordinate[2]
    || a.coordinate[0] - b.coordinate[0]);

  const brickFaces = [];
  const addNonBrickPlacement = (cell, kind, normal) => {
    const faceCell = { ...cell, kind, normal };
    const basePosition = flushPosition(faceCell, kind, settings);
    placements.push({
      id: placements.length,
      kind,
      coordinate: [...cell.coordinate],
      sourceCellId: cell.id,
      position: basePosition,
      normal: normal.clone(),
      span: [1, 1, 1],
      occupiedCells: 1,
    });
  };

  for (const cell of ordered) {
    if (cell.kind === "brick") brickFaces.push({ cell, normal: cell.normal.clone() });
    else addNonBrickPlacement(cell, cell.kind, cell.normal);
    (cell.secondaryFaces || []).forEach((secondary) => {
      if (secondary.kind === "brick") brickFaces.push({ cell, normal: secondary.normal.clone() });
      else addNonBrickPlacement(cell, secondary.kind, secondary.normal);
    });
  }

  const brick = state.blocks.brick;
  if (!brickFaces.length || !brick) return placements.map((placement, id) => ({ ...placement, id }));
  const metrics = brickCourseMetrics(settings);
  const planes = new Map();
  for (const entry of brickFaces) {
    const face = snapFace(entry.normal);
    const normalAxis = Math.floor(faceIndex(face) / 2);
    const planeKey = `${faceIndex(face)}|${entry.cell.coordinate[normalAxis]}`;
    if (!planes.has(planeKey)) planes.set(planeKey, []);
    planes.get(planeKey).push({ ...entry, face });
  }

  for (const planeEntries of planes.values()) {
    const face = planeEntries[0].face;
    const rotation = orientationFor("brick", face);
    const longWorldAxis = dominantWorldAxis(assetLongAxis(brick), rotation);
    const shortWorldAxis = dominantWorldAxis(assetShortAxis(brick), rotation);
    const normalAxis = Math.floor(faceIndex(face) / 2);
    const occupied = new Map();
    planeEntries.forEach((entry) => {
      const u = entry.cell.coordinate[longWorldAxis];
      const v = entry.cell.coordinate[shortWorldAxis];
      occupied.set(`${u},${v}`, entry.cell);
    });
    const planeMinimumU = Math.min(...planeEntries.map((entry) => entry.cell.coordinate[longWorldAxis]));
    const planeLatticeOrigin = planeMinimumU * settings.cell.getComponent(longWorldAxis);

    const remaining = new Set(occupied.keys());
    while (remaining.size) {
      const firstKey = remaining.values().next().value;
      const componentKeys = [];
      const queue = [firstKey];
      remaining.delete(firstKey);
      while (queue.length) {
        const key = queue.pop();
        componentKeys.push(key);
        const [u, v] = key.split(",").map(Number);
        for (const neighbor of [`${u - 1},${v}`, `${u + 1},${v}`, `${u},${v - 1}`, `${u},${v + 1}`]) {
          if (!remaining.has(neighbor)) continue;
          remaining.delete(neighbor);
          queue.push(neighbor);
        }
      }
      const componentCells = componentKeys.map((key) => occupied.get(key));
      const referenceCell = componentCells[0];
      const planeOrigin = [0, 1, 2].map((axis) => (
        referenceCell.position.getComponent(axis)
        - (referenceCell.coordinate[axis] + 0.5) * settings.cell.getComponent(axis)
      ));
      const minimumV = Math.min(...componentCells.map((cell) => cell.coordinate[shortWorldAxis]));
      const maximumV = Math.max(...componentCells.map((cell) => cell.coordinate[shortWorldAxis]));
      const componentCellByKey = new Map(componentCells.map((cell) => [
        `${cell.coordinate[longWorldAxis]},${cell.coordinate[shortWorldAxis]}`,
        cell,
      ]));

      for (let cellV = minimumV; cellV <= maximumV; cellV++) {
        for (let subRow = 0; subRow < metrics.rowsPerCell; subRow++) {
          const course = cellV * metrics.rowsPerCell + subRow;
          const occupiedU = componentCells
            .filter((cell) => cell.coordinate[shortWorldAxis] === cellV)
            .map((cell) => cell.coordinate[longWorldAxis])
            .sort((a, b) => a - b);
          if (!occupiedU.length) continue;
          const runs = [];
          for (const u of occupiedU) {
            const last = runs[runs.length - 1];
            if (!last || u > last.maximum + 1) runs.push({ minimum: u, maximum: u });
            else last.maximum = u;
          }
          const verticalCenter = (cellV + (subRow + 0.5) / metrics.rowsPerCell) * settings.cell.getComponent(shortWorldAxis);
          for (const run of runs) {
            const runMinimum = run.minimum * settings.cell.getComponent(longWorldAxis);
            const runMaximum = (run.maximum + 1) * settings.cell.getComponent(longWorldAxis);
            const latticeOrigin = planeLatticeOrigin + (course % 2 ? metrics.length * 0.5 : 0);
            const firstBrick = Math.floor((runMinimum - latticeOrigin) / metrics.length) - 1;
            const lastBrick = Math.ceil((runMaximum - latticeOrigin) / metrics.length) + 1;
            for (let brickIndex = firstBrick; brickIndex <= lastBrick; brickIndex++) {
              const nominalMinimum = latticeOrigin + brickIndex * metrics.length;
              const nominalMaximum = nominalMinimum + metrics.length;
              const visibleMinimum = Math.max(nominalMinimum, runMinimum);
              const visibleMaximum = Math.min(nominalMaximum, runMaximum);
              const visibleLength = visibleMaximum - visibleMinimum;
              if (visibleLength <= 1e-7) continue;
              const midpoint = (visibleMinimum + visibleMaximum) * 0.5;
              const ownerU = THREE.MathUtils.clamp(
                Math.floor(midpoint / settings.cell.getComponent(longWorldAxis)),
                run.minimum,
                run.maximum,
              );
              const owner = componentCellByKey.get(`${ownerU},${cellV}`) || componentCells[0];
              const faceCell = { ...owner, kind: "brick", normal: face };
              const position = flushPosition(faceCell, "brick", settings);
              position.setComponent(longWorldAxis, planeOrigin[longWorldAxis] + midpoint);
              position.setComponent(shortWorldAxis, planeOrigin[shortWorldAxis] + verticalCenter);
              placements.push({
                id: placements.length,
                kind: "brick",
                coordinate: [...owner.coordinate],
                sourceCellId: owner.id,
                subCell: { course, brick: brickIndex },
                position,
                normal: face.clone(),
                span: [0, 0, 0].map((_, axis) => (
                  axis === longWorldAxis ? visibleLength / settings.cell.getComponent(longWorldAxis)
                    : axis === shortWorldAxis ? 1 / metrics.rowsPerCell
                      : 1
                )),
                occupiedCells: Math.max(1, run.maximum - run.minimum + 1),
                scaleMultiplier: [0, 0, 0].map((_, axis) => (axis === assetLongAxis(brick) ? visibleLength / metrics.length : 1)),
                runningBond: {
                  course,
                  offset: course % 2 ? 0.5 : 0,
                  nominalLength: metrics.length,
                  visibleLength,
                  cut: visibleLength < metrics.length - 1e-7,
                },
              });
            }
          }
        }
      }
    }
  }
  return placements;
}

async function buildStructure() {
  if (!state.sourceRoot || !Object.values(state.blocks).every(Boolean)) return;
  const settings = readSettings();
  state.buildSettings = settings;
  normalizeSource();
  state.sourceRoot.updateMatrixWorld(true);
  state.sourceBounds = new THREE.Box3().setFromObject(state.sourceRoot);
  const origin = new THREE.Vector3(
    Math.floor(state.sourceBounds.min.x / settings.cell.x) * settings.cell.x,
    Math.floor(state.sourceBounds.min.y / settings.cell.y) * settings.cell.y,
    Math.floor(state.sourceBounds.min.z / settings.cell.z) * settings.cell.z,
  );
  const cellMap = new Map();
  const detailFaceWeights = new Map();
  const details = [];
  const meshes = [];
  state.sourceRoot.traverse((object) => { if (object.isMesh && object.geometry?.attributes?.position) meshes.push(object); });

  try {
    showProgress("Analyzing geometry…", `0 / ${meshes.length} meshes`, 0);
    for (let meshIndex = 0; meshIndex < meshes.length; meshIndex++) {
      const mesh = meshes[meshIndex];
      const geometry = mesh.geometry;
      const position = geometry.attributes.position;
      const index = geometry.index;
      const materials = Array.isArray(mesh.material) ? mesh.material : [mesh.material];
      const objectIsDetail = settings.keepDetails && includesAny(hierarchyName(mesh), settings.detailKeywords);
      const groups = getGeometryGroups(mesh);
      for (const group of groups) {
        const material = materials[group.materialIndex] || materials[0];
        const uv = textureUvAttribute(geometry, material);
        const mappedKind = objectIsDetail ? "detail" : (state.materialMapping.get(material?.uuid) || inferKind(material, mesh.name));
        if (mappedKind === "ignore") continue;
        if (mappedKind === "detail") {
          if (settings.keepDetails) {
            details.push(extractDetailPrimitive(mesh, group, material));
            markDetailGroupCells(mesh, group, detailFaceWeights, origin, settings);
          }
          continue;
        }
        const end = Math.min(group.start + group.count, index ? index.count : position.count);
        const textureDetails = [];
        const a = new THREE.Vector3();
        const b = new THREE.Vector3();
        const c = new THREE.Vector3();
        const uvA = new THREE.Vector2();
        const uvB = new THREE.Vector2();
        const uvC = new THREE.Vector2();
        for (let cursor = group.start; cursor + 2 < end; cursor += 3) {
          const ia = index ? index.getX(cursor) : cursor;
          const ib = index ? index.getX(cursor + 1) : cursor + 1;
          const ic = index ? index.getX(cursor + 2) : cursor + 2;
          a.fromBufferAttribute(position, ia).applyMatrix4(mesh.matrixWorld);
          b.fromBufferAttribute(position, ib).applyMatrix4(mesh.matrixWorld);
          c.fromBufferAttribute(position, ic).applyMatrix4(mesh.matrixWorld);
          if (mappedKind === "auto" && uv) {
            uvA.fromBufferAttribute(uv, ia);
            uvB.fromBufferAttribute(uv, ib);
            uvC.fromBufferAttribute(uv, ic);
          }
          const classification = {
            kind: mappedKind,
            material,
            uvA: mappedKind === "auto" && uv ? uvA : null,
            uvB: mappedKind === "auto" && uv ? uvB : null,
            uvC: mappedKind === "auto" && uv ? uvC : null,
          };
          let triangleKind = classifyTriangleKind(classification);
          if (triangleKind === "detail") {
            if (settings.keepDetails) {
              textureDetails.push([ia, ib, ic]);
              markDetailTriangleCells(detailFaceWeights, a, b, c, origin, settings);
              continue;
            }
            triangleKind = "concrete";
          }
          sampleTriangle(cellMap, a, b, c, origin, settings, triangleKind);
          if (cellMap.size > settings.budget * 1.25) {
            throw new Error(`Estimated block count exceeds the ${formatNumber(settings.budget)} budget. Increase Cell X/Y/Z.`);
          }
        }
        if (textureDetails.length) {
          splitTriangleComponents(textureDetails).forEach((component, componentIndex) => {
            details.push(extractDetailTriangles(mesh, component, material, componentIndex));
          });
        }
      }
      showProgress("Analyzing geometry…", `${meshIndex + 1} / ${meshes.length} meshes · ${formatNumber(cellMap.size)} cells`, (meshIndex + 1) / meshes.length * 0.72);
      await nextFrame();
    }

    if (!cellMap.size) throw new Error("No structural surfaces found. Check the material mapping.");
    const envelope = resolveSurfaceEnvelope(cellMap, detailFaceWeights, origin, settings);
    if (!envelope.cells.length) throw new Error("No outer structural surface found. Check the material mapping.");
    const sampledCells = regularizeSurfaceCells(envelope.cells, envelope.protectedDetailRays, origin, settings);
    if (!sampledCells.length) throw new Error("The grid is too coarse to form a connected outer shell. Reduce Cell X/Y/Z.");
    if (sampledCells.length > settings.budget) throw new Error(`Result has ${formatNumber(sampledCells.length)} occupied cells. Increase cell size or raise the block budget.`);
    const gridCells = cleanMaterialRegions(sampledCells);
    const cells = tileStructuralCells(gridCells, settings);
    if (cells.length > 60000) throw new Error(`Result needs ${formatNumber(cells.length)} physical assets. Increase Cell X/Y/Z to keep Unity performance manageable.`);
    state.gridCells = gridCells;
    state.cells = cells;
    state.details = details;
    showProgress("Creating preview…", `${formatNumber(cells.length)} blocks`, 0.84);
    await createPreview(cells, details, settings);
    showProgress("Finalizing…", "Preparing Unity export metadata", 0.98);
    await nextFrame();

    els.viewResult.disabled = false;
    els.downloadJson.disabled = false;
    els.downloadFbx.disabled = false;
    const counts = countKinds(cells);
    els.resultStatus.textContent = `Brick ${formatNumber(counts.brick)} · Concrete ${formatNumber(counts.concrete)} · Glass ${formatNumber(counts.glass)} · Details ${details.length}`;
    els.resultCount.textContent = `${formatNumber(cells.length)} assets · ${formatNumber(gridCells.length)} occupied cells`;
    setView("result");
    showToast(`Built ${formatNumber(cells.length)} blocks. FBX export is ready.`);
  } catch (error) {
    console.error(error);
    showToast(error.message, true);
  } finally {
    hideProgress();
  }
}

function countKinds(cells) {
  const result = { brick: 0, concrete: 0, glass: 0 };
  cells.forEach((cell) => result[cell.kind]++);
  return result;
}

async function createPreview(cells, details, settings) {
  clearGroup(resultDisplay, true);
  state.previewMeshes = [];
  const byKind = { brick: [], concrete: [], glass: [] };
  cells.forEach((cell) => byKind[cell.kind].push(cell));
  for (const kind of Object.keys(byKind)) {
    const kindCells = byKind[kind];
    if (!kindCells.length) continue;
    const asset = state.blocks[kind];
    asset.object.updateMatrixWorld(true);
    const rootInverse = asset.object.matrixWorld.clone().invert();
    const components = [];
    asset.object.traverse((object) => {
      if (object.isMesh && object.geometry) {
        components.push({
          mesh: object,
          relativeMatrix: rootInverse.clone().multiply(object.matrixWorld),
        });
      }
    });
    for (let componentIndex = 0; componentIndex < components.length; componentIndex++) {
      const component = components[componentIndex];
      const sourceMaterials = Array.isArray(component.mesh.material) ? component.mesh.material : [component.mesh.material];
      const previewMaterials = sourceMaterials.map((material) => {
        const clone = material?.clone?.() || new THREE.MeshStandardMaterial({ color: TYPE_COLORS[kind] });
        if (kind === "glass") {
          clone.transparent = true;
          clone.opacity = Math.min(clone.opacity ?? 1, 0.7);
        }
        return clone;
      });
      const instanced = new THREE.InstancedMesh(
        component.mesh.geometry.clone(),
        previewMaterials.length === 1 ? previewMaterials[0] : previewMaterials,
        kindCells.length,
      );
      instanced.name = `${kind}_preview_${componentIndex}`;
      const wrapperMatrix = new THREE.Matrix4();
      const visualMatrix = new THREE.Matrix4();
      const finalMatrix = new THREE.Matrix4();
      kindCells.forEach((cell, index) => {
        const visualScale = fittedScale(kind, settings).multiply(new THREE.Vector3(...(cell.scaleMultiplier || [1, 1, 1])));
        const visualOffset = asset.center.clone().multiply(visualScale).negate();
        visualMatrix.compose(visualOffset, new THREE.Quaternion(), visualScale);
        wrapperMatrix.compose(cell.position, orientationFor(kind, cell.normal), new THREE.Vector3(1, 1, 1));
        finalMatrix.copy(wrapperMatrix).multiply(visualMatrix).multiply(component.relativeMatrix);
        instanced.setMatrixAt(index, finalMatrix);
      });
      instanced.instanceMatrix.needsUpdate = true;
      resultDisplay.add(instanced);
      state.previewMeshes.push(instanced);
    }
  }
  if (details.length) {
    const detailGroup = new THREE.Group();
    detailGroup.name = "Separated_Details_Preview";
    details.forEach((detail) => detailGroup.add(detail.clone()));
    resultDisplay.add(detailGroup);
  }
  applyWireframe();
}

function createExportScene() {
  const exportScene = new THREE.Scene();
  exportScene.name = `${fileStem(state.sourceFile?.name)}_Smash`;
  const roots = {
    brick: new THREE.Group(),
    concrete: new THREE.Group(),
    glass: new THREE.Group(),
  };
  roots.brick.name = "Blocks_Brick";
  roots.concrete.name = "Blocks_Concrete";
  roots.glass.name = "Blocks_Glass";
  Object.values(roots).forEach((root) => exportScene.add(root));

  for (const cell of state.cells) {
    const asset = state.blocks[cell.kind];
    const wrapper = new THREE.Group();
    wrapper.name = `${cell.kind[0].toUpperCase()}${cell.kind.slice(1)}_${String(cell.id).padStart(5, "0")}`;
    wrapper.position.copy(cell.position);
    wrapper.quaternion.copy(orientationFor(cell.kind, cell.normal));
    wrapper.userData.SmashType = cell.kind;
    wrapper.userData.CellId = cell.id;

    const visual = asset.object.clone(true);
    visual.name = `${wrapper.name}_Visual`;
    const scale = fittedScale(cell.kind, state.buildSettings).multiply(new THREE.Vector3(...(cell.scaleMultiplier || [1, 1, 1])));
    visual.scale.copy(scale);
    visual.position.copy(asset.center).multiply(scale).negate();
    visual.traverse((object) => {
      if (!object.isMesh) return;
      object.castShadow = false;
      object.receiveShadow = false;
    });
    wrapper.add(visual);
    roots[cell.kind].add(wrapper);
  }

  if (state.details.length) {
    const detailRoot = new THREE.Group();
    detailRoot.name = "Separated_Details";
    state.details.forEach((detail, index) => {
      const clone = detail.clone();
      clone.name = `Detail_${String(index).padStart(4, "0")}_${detail.name}`;
      detailRoot.add(clone);
    });
    exportScene.add(detailRoot);
  }
  exportScene.updateMatrixWorld(true);
  return exportScene;
}

function buildLayout() {
  const settings = state.buildSettings;
  const brickMetrics = brickCourseMetrics(settings);
  const layoutCells = state.gridCells.length ? state.gridCells : state.cells;
  const assets = {};
  Object.entries(state.blocks).forEach(([kind, asset]) => {
    const instanceScale = fittedScale(kind, settings);
    assets[kind] = {
      source: asset.fileName,
      measuredSize: asset.size.toArray(),
      measuredCenter: asset.center.toArray(),
      thinAxis: asset.thinAxis,
      longAxis: assetLongAxis(asset),
      commonKitScale: commonKitScale(settings),
      meshSeamScale: commonKitScale(settings) * KIT_SEAM_OVERLAP,
      instanceScale: instanceScale.toArray(),
      scaleMode: kind === "brick" ? "uniform-running-bond-course-fit" : "common-uniform",
      scaledSize: asset.size.clone().multiply(instanceScale).toArray(),
    };
  });

  const minimum = [Infinity, Infinity, Infinity];
  const maximum = [-Infinity, -Infinity, -Infinity];
  layoutCells.forEach((cell) => {
    cell.coordinate.forEach((value, axis) => {
      minimum[axis] = Math.min(minimum[axis], value);
      maximum[axis] = Math.max(maximum[axis], value);
    });
  });

  const firstCell = layoutCells[0];
  const firstGrid = firstCell.coordinate.map((value, axis) => value - minimum[axis]);
  const origin = firstCell.position.clone().sub(new THREE.Vector3(
    firstGrid[0] * settings.cell.x,
    firstGrid[1] * settings.cell.y,
    firstGrid[2] * settings.cell.z,
  ));
  const faceName = (normal) => {
    const face = snapFace(normal);
    if (Math.abs(face.x) > 0.5) return face.x > 0 ? "+X" : "-X";
    if (Math.abs(face.y) > 0.5) return face.y > 0 ? "+Y" : "-Y";
    return face.z > 0 ? "+Z" : "-Z";
  };
  const layerMap = new Map();
  layoutCells.forEach((cell) => {
    const x = cell.coordinate[0] - minimum[0];
    const y = cell.coordinate[1] - minimum[1];
    const layerIndex = cell.coordinate[2] - minimum[2];
    if (!layerMap.has(layerIndex)) layerMap.set(layerIndex, []);
    layerMap.get(layerIndex).push({
      id: cell.id,
      x,
      y,
      material: cell.kind,
      face: faceName(cell.normal),
    });
  });
  const layers = [...layerMap.entries()]
    .sort(([a], [b]) => a - b)
    .map(([index, cells]) => ({
      index,
      cells: cells.sort((a, b) => a.y - b.y || a.x - b.x),
    }));

  return {
    format: "SmashBuildingLayerMap",
    version: 2,
    source: state.sourceFile?.name || "Building.glb",
    generatedAt: new Date().toISOString(),
    unitMeters: 1,
    grid: {
      axes: { columns: "X", rows: "Y", layers: "Z" },
      dimensions: {
        x: maximum[0] - minimum[0] + 1,
        y: maximum[1] - minimum[1] + 1,
        layers: maximum[2] - minimum[2] + 1,
      },
      cellSize: { x: settings.cell.x, y: settings.cell.y, layer: settings.cell.z },
      originCellCenter: { x: origin.x, y: origin.y, z: origin.z },
      blockGap: settings.gap,
      commonUniformKitScale: commonKitScale(settings),
      meshSeamScale: commonKitScale(settings) * KIT_SEAM_OVERLAP,
      fullBrickProportionsFixed: true,
      boundaryPiecesShortened: true,
      brickScaleReference: "The Brick FBX proportions are preserved uniformly; 9 Brick = 1 Concrete is a visual size reference, not a placement count.",
      surfaceDensity: settings.density,
    },
    assets,
    layers,
    tiling: {
      exportedAssetCount: state.cells.length,
      occupiedCellCount: layoutCells.length,
      brickBond: {
        pattern: "running-bond",
        alternateCourseOffset: 0.5,
        rowsPerLogicalCell: brickMetrics.rowsPerCell,
        nominalLength: brickMetrics.length,
        nominalHeight: brickMetrics.height,
        edgeTreatment: "shorten boundary piece to the occupied Brick mask",
      },
      seamOverlap: KIT_SEAM_OVERLAP,
      note: "Concrete and Glass use one placement per occupied cell. Brick is tiled horizontally across each coplanar wall region with every other course shifted by half a brick; boundary pieces are shortened to stay inside the Brick mask.",
    },
    physicalPlacements: state.cells.map((cell) => ({
      id: cell.id,
      sourceCellId: cell.sourceCellId,
      material: cell.kind,
      sourceGrid: {
        x: cell.coordinate[0] - minimum[0],
        y: cell.coordinate[1] - minimum[1],
        layer: cell.coordinate[2] - minimum[2],
      },
      subCell: cell.subCell || null,
      position: { x: cell.position.x, y: cell.position.y, z: cell.position.z },
      face: faceName(cell.normal),
      span: cell.span,
      instanceScale: fittedScale(cell.kind, settings).multiply(new THREE.Vector3(...(cell.scaleMultiplier || [1, 1, 1]))).toArray(),
      runningBond: cell.runningBond || null,
    })),
    separatedDetails: state.details.map((detail) => detail.name),
    unity: {
      positionFormula: "world = originCellCenter + (x * cellSize.x, y * cellSize.y, layer.index * cellSize.layer)",
      materialField: "Each cell.material is brick, concrete or glass.",
      note: "Cell Z is implicit in layers. Use physicalPlacements to reproduce the exact staggered running-bond Brick packing and shortened boundary pieces.",
      hierarchy: ["Blocks_Brick", "Blocks_Concrete", "Blocks_Glass", "Separated_Details"],
    },
  };
}

function downloadBlob(blob, name) {
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement("a");
  anchor.href = url;
  anchor.download = name;
  anchor.click();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}

function downloadLayout() {
  if (!state.cells.length) return;
  const json = JSON.stringify(buildLayout(), null, 2);
  downloadBlob(new Blob([json], { type: "application/json" }), `${fileStem(state.sourceFile.name)}_layers.json`);
}

async function downloadFbx() {
  if (!state.cells.length) return;
  let exportScene = null;
  try {
    showProgress("Building FBX hierarchy…", `${formatNumber(state.cells.length)} named block objects`, 0.12);
    await nextFrame();
    exportScene = createExportScene();
    showProgress("Encoding Unity FBX…", "Embedding meshes and materials in binary FBX 7.4", 0.55);
    await nextFrame();
    const { FBXExporter } = await import("@comfyorg/fbx-exporter-three");
    const bytes = await new FBXExporter().parseAsync(exportScene, {
      preset: "unity",
      version: 7400,
      includeAnimations: false,
      embedTextures: true,
      customProperties: true,
      creator: "Smash Builder Web 1.0",
    });
    showProgress("Saving FBX…", `${(bytes.byteLength / 1024 / 1024).toFixed(1)} MB`, 0.96);
    downloadBlob(new Blob([bytes], { type: "application/octet-stream" }), `${fileStem(state.sourceFile.name)}_Smash.fbx`);
    showToast("Unity FBX downloaded. Keep the layout JSON beside it for metadata.");
  } catch (error) {
    console.error(error);
    showToast(`FBX export failed: ${error.message}`, true);
  } finally {
    hideProgress();
    if (exportScene) clearGroup(exportScene, false);
  }
}

function bindDropZone(element, callback) {
  ["dragenter", "dragover"].forEach((eventName) => element.addEventListener(eventName, (event) => {
    event.preventDefault();
    element.classList.add("dragging");
  }));
  ["dragleave", "drop"].forEach((eventName) => element.addEventListener(eventName, (event) => {
    event.preventDefault();
    element.classList.remove("dragging");
  }));
  element.addEventListener("drop", (event) => callback(event.dataTransfer.files[0]));
}

els.sourceInput.addEventListener("change", () => loadSourceFile(els.sourceInput.files[0]));
bindDropZone(els.sourceDrop, loadSourceFile);
els.brickInput.addEventListener("change", () => replaceBlockAsset("brick", els.brickInput.files[0]));
els.concreteInput.addEventListener("change", () => replaceBlockAsset("concrete", els.concreteInput.files[0]));
els.glassInput.addEventListener("change", () => replaceBlockAsset("glass", els.glassInput.files[0]));
els.buildButton.addEventListener("click", buildStructure);
els.downloadJson.addEventListener("click", downloadLayout);
els.downloadFbx.addEventListener("click", downloadFbx);
els.viewSource.addEventListener("click", () => setView("source"));
els.viewResult.addEventListener("click", () => setView("result"));
els.frameButton.addEventListener("click", () => fitCamera());
els.gridButton.addEventListener("click", () => {
  grid.visible = !grid.visible;
  els.gridButton.classList.toggle("active", grid.visible);
});
els.wireButton.addEventListener("click", () => {
  state.wireframe = !state.wireframe;
  els.wireButton.classList.toggle("active", state.wireframe);
  applyWireframe();
});
els.density.addEventListener("input", () => { els.densityOutput.textContent = `${Number(els.density.value).toFixed(1)}×`; });
$("#ground-align").addEventListener("change", () => {
  normalizeSource();
  if (state.sourceBounds) els.statDimensions.textContent = formatSize(state.sourceBounds.getSize(new THREE.Vector3()));
  fitCamera(sourceDisplay);
});

resizeRenderer();
loadDefaultBlockAssets();
