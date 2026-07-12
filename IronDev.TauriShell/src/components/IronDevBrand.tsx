import markUrl from '../../brand/irondev-mark.svg';

interface IronDevBrandProps {
  descriptor?: boolean;
}

export function IronDevBrand({ descriptor = false }: IronDevBrandProps) {
  return (
    <span className="fl-brand" aria-label={descriptor ? 'IronDev, governed engineering' : 'IronDev'}>
      <img className="fl-brand-mark" src={markUrl} alt="" aria-hidden="true" />
      <span className="fl-brand-copy">
        <strong>IronDev</strong>
        {descriptor ? <small>Governed engineering</small> : null}
      </span>
    </span>
  );
}
