-- Why an item left the list (ADR-038): bought it, or no longer needed. Without this, purchase
-- patterns ("how often do we buy milk") would count things that were never bought.
ALTER TABLE shopping.items ADD COLUMN check_reason text CHECK (check_reason IN ('bought', 'not_needed'));

-- Everything ticked before this column existed was recorded as a purchase; keep it that way.
UPDATE shopping.items SET check_reason = 'bought' WHERE checked_at IS NOT NULL;

ALTER TABLE shopping.items ADD CONSTRAINT ck_items_check_reason CHECK ((checked_at IS NULL) = (check_reason IS NULL));
