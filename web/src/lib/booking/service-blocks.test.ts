import { describe, expect, it } from "vitest";
import { buildServiceBlocks } from "./service-blocks";
import type {
  BookingServiceDto,
  ServiceCategoryDto,
} from "../booking-openapi-client";

const svc = (
  id: string,
  categoryId?: string,
  orderIndex?: number,
): BookingServiceDto => ({
  id,
  categoryId,
  name: id,
  orderIndex,
});
const cat = (id: string, orderIndex = 0): ServiceCategoryDto => ({
  id,
  name: id,
  orderIndex,
});

describe("buildServiceBlocks", () => {
  it("no categories → single flat '_all' block", () => {
    const blocks = buildServiceBlocks([svc("a"), svc("b")], []);
    expect(blocks).toHaveLength(1);
    expect(blocks[0].key).toBe("_all");
    expect(blocks[0].items.map((s) => s.id)).toEqual(["a", "b"]);
  });

  it("services reference non-existent categories → still flat '_all'", () => {
    const blocks = buildServiceBlocks([svc("a", "ghost")], []);
    expect(blocks).toHaveLength(1);
    expect(blocks[0].key).toBe("_all");
  });

  it("orders category blocks by orderIndex", () => {
    const blocks = buildServiceBlocks(
      [svc("a", "c2"), svc("b", "c1")],
      [cat("c1", 1), cat("c2", 0)],
    );
    expect(blocks.map((b) => b.key)).toEqual(["c2", "c1"]);
  });

  it("mixed: categorized + uncategorized → category blocks + collapsible '_other'", () => {
    const blocks = buildServiceBlocks(
      [svc("a", "c1"), svc("orphan")],
      [cat("c1", 0)],
    );
    expect(blocks.map((b) => b.key)).toEqual(["c1", "_other"]);
    expect(blocks[1].title).toBe("Inne usługi");
    expect(blocks[1].items.map((s) => s.id)).toEqual(["orphan"]);
  });

  it("empty services → no blocks", () => {
    expect(buildServiceBlocks([], [])).toEqual([]);
  });

  it("sorts services within a category by orderIndex", () => {
    const blocks = buildServiceBlocks(
      [svc("a", "c1", 2), svc("b", "c1", 0), svc("c", "c1", 1)],
      [cat("c1", 0)],
    );
    expect(blocks[0].items.map((s) => s.id)).toEqual(["b", "c", "a"]);
  });

  it("stable sort: equal orderIndex keeps input order", () => {
    const blocks = buildServiceBlocks(
      [svc("a", "c1", 0), svc("b", "c1", 0), svc("c", "c1", 0)],
      [cat("c1", 0)],
    );
    expect(blocks[0].items.map((s) => s.id)).toEqual(["a", "b", "c"]);
  });

  it("sorts flat '_all' block by orderIndex", () => {
    const blocks = buildServiceBlocks([svc("a", undefined, 2), svc("b", undefined, 1)], []);
    expect(blocks[0].key).toBe("_all");
    expect(blocks[0].items.map((s) => s.id)).toEqual(["b", "a"]);
  });

  it("sorts orphan '_other' block by orderIndex", () => {
    const blocks = buildServiceBlocks(
      [svc("a", "c1", 0), svc("z", undefined, 2), svc("y", undefined, 1)],
      [cat("c1", 0)],
    );
    expect(blocks.map((b) => b.key)).toEqual(["c1", "_other"]);
    expect(blocks[1].items.map((s) => s.id)).toEqual(["y", "z"]);
  });
});
