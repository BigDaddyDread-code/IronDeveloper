export function HumanReviewChecklist({ items }: { items: string[] }) {
  return (
    <section className="chat-build-checklist" data-testid="chat-build.humanReviewChecklist">
      <div className="section-heading">
        <p className="eyebrow">Human review</p>
        <h2>Checklist</h2>
      </div>
      {items.length > 0 ? (
        <ul>
          {items.map((item) => (
            <li key={item}>{item}</li>
          ))}
        </ul>
      ) : (
        <p className="state-muted">No human review checklist has been loaded.</p>
      )}
    </section>
  );
}
