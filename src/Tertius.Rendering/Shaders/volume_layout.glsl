// The shape of the directional light volume's grids (LightVolumePass holds the C# copy of these numbers). Three nested
// grids, finest first, in the axes of the camera's reference frame. Each grid's receivers use a reach of 16 cells per
// axis. The two inner grids store exactly that. The outer grid also stores a guard shell of one cell on each face, so
// 18 cells per axis: its reach can then follow the camera continuously and still read only stored cells
// (volume_receive.glsl, levelWeight).
#ifndef TERTIUS_VOLUME_LAYOUT_GLSL
#define TERTIUS_VOLUME_LAYOUT_GLSL

const int Levels = 3;
const int Reach = 16;
const int MaxSide = 18;
const int Directions = 32;
// 2 × 16³ + 18³ cells and 2 × 4³ + 6³ lighting groups.
const int CellCount = 14024;
const int TileCount = 344;
const int MaxLevelTiles = 216;

// The cells beyond the reach on each face of a level's grid.
int guardOf(int level)
{
    return level == Levels - 1 ? 1 : 0;
}
int sideOf(int level)
{
    return Reach + 2 * guardOf(level);
}
int cellsOf(int level)
{
    int side = sideOf(level);
    return side * side * side;
}
// Every level before the outer one holds Reach³ cells.
int firstCellOf(int level)
{
    return level * Reach * Reach * Reach;
}
int levelOfCell(int cell)
{
    int level = 0;
    while (level + 1 < Levels && cell >= firstCellOf(level + 1))
        ++level;
    return level;
}
int cellIndex(int level, ivec3 c)
{
    int side = sideOf(level);
    return c.x + side * (c.y + side * c.z);
}
ivec3 coordinates(int level, int i)
{
    int side = sideOf(level);
    return ivec3(i % side, (i / side) % side, i / (side * side));
}
bool withinGrid(int level, ivec3 c)
{
    return all(greaterThanEqual(c, ivec3(0))) && all(lessThan(c, ivec3(sideOf(level))));
}
// The index of a direction's transfer at a cell of a level: each level holds its directions one after another.
int fieldIndex(int level, int direction, int cell)
{
    return Directions * firstCellOf(level) + direction * cellsOf(level) + cell;
}

// The lights are evaluated once per lighting group and surface side (volume_shade.frag). A group is a block of up to
// 4 × 4 × 4 cells whose faces lie at whole multiples of 4 cells from the grid's center. So the 16 central cells of the
// outer grid keep the groups that a 16-cell grid has, and its guard shell forms groups one cell deep.
int tilesPerAxisOf(int level)
{
    return 2 * ((sideOf(level) / 2 + 3) / 4);
}
int tileShiftOf(int level)
{
    return 2 * tilesPerAxisOf(level) - sideOf(level) / 2;
}
int tilesOf(int level)
{
    int n = tilesPerAxisOf(level);
    return n * n * n;
}
// Every level before the outer one holds 4³ groups.
int firstTileOf(int level)
{
    return level * 64;
}
int tileIndex(int level, ivec3 c)
{
    int n = tilesPerAxisOf(level);
    ivec3 t = (c + tileShiftOf(level)) / 4;
    return firstTileOf(level) + t.x + n * (t.y + n * t.z);
}
// The lowest cell of a level's group `tile`, which can lie outside the grid for a group of the guard shell.
ivec3 tileOrigin(int level, int tile)
{
    int n = tilesPerAxisOf(level);
    return 4 * ivec3(tile % n, (tile / n) % n, tile / (n * n)) - tileShiftOf(level);
}

#endif
